"""DHG-regresjon mot en lokal syntetisk server, uten eksterne API-kall.

Kjør etter publisering: python verification/dhg-http-smoke.py --app-dir publish
Starter både testkilden og appen på midlertidige loopback-porter og rydder opp etterpå.
--browser kjører i tillegg browser-smoke.py mot samme app; krever Playwright og nettleser.
"""
import argparse
import copy
import json
import os
from pathlib import Path
import socket
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = Path(__file__).resolve().parents[1]
# Dokumenterte syntetiske DHG-testpersoner; ressursene lastes fra repoets lokale eksempelfiler.
IDENTIFIERS = ['29760484634', '11859699482']
PATIENT = json.loads((ROOT / 'examples/dhg-patient.json').read_text(encoding='utf-8'))
RESOURCES = json.loads((ROOT / 'examples/dhg-resources.json').read_text(encoding='utf-8'))['entry']
QUESTIONNAIRE = json.loads((ROOT / 'examples/questionnaire-dhg.json').read_text(encoding='utf-8'))


def bundle(resources):
    """Lag et komplett searchset med samsvar mellom total og entry, også for null treff."""
    result = {'resourceType': 'Bundle', 'type': 'searchset', 'total': len(resources)}
    if resources:
        result['entry'] = [{'resource': r} for r in resources]
    return result


class DhgMock(BaseHTTPRequestHandler):
    """Simuler POST-kontrakten og valgte kildefeil; calls brukes til å kontrollere faktiske HTTP-kall."""
    calls = []
    mode = 'normal'

    def log_message(self, *_):
        # Slå av standard tilgangslogg, slik at identifikatorer og data ikke skrives ut.
        pass

    def send_json(self, value, status=200):
        body = json.dumps(value).encode('utf-8')
        self.send_response(status)
        self.send_header('Content-Type', 'application/fhir+json')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        self.calls.append(('GET', self.path, {}))
        if self.path != '/fhir/metadata':
            self.send_json({'resourceType': 'OperationOutcome'}, 405)
            return
        assert not self.headers.get('Authorization') and not self.headers.get('Content-Length')
        assert self.headers['Accept'] == 'application/fhir+json'
        status = {'status-auth': 401, 'status-missing': 404, 'status-redirect': 302, 'status-error': 503}.get(self.mode, 200)
        if status == 200:
            if self.mode == 'status-not-fhir':
                self.send_json({'resourceType': 'OperationOutcome'})
            else:
                self.send_json({'resourceType': 'CapabilityStatement',
                                'fhirVersion': '5.0.0' if self.mode == 'status-not-r4' else '4.0.1'})
            return
        self.send_response(status)
        if status == 302:
            self.send_header('Location', '/must-not-follow')
        self.end_headers()

    def do_POST(self):
        raw = self.rfile.read(int(self.headers.get('Content-Length', 0)))
        form = urllib.parse.parse_qs(raw.decode('utf-8'), strict_parsing=True)
        self.calls.append(('POST', self.path, form))
        route = self.path.split('/')
        assert len(route) == 4 and route[1] == 'fhir' and route[3] == '_search'
        resource_type = route[2]
        assert resource_type in ('Patient', 'Observation', 'Encounter', 'CareTeam')
        assert self.headers['Content-Type'] == 'application/x-www-form-urlencoded'
        assert self.headers['Accept'] == 'application/fhir+json'
        assert all(not self.headers.get(h) for h in ('Authorization', 'DPoP', 'X-Patient-Context'))
        assert len(raw) <= 4096
        key = 'identifier' if resource_type == 'Patient' else 'patient.identifier'
        assert set(form) == {key} and len(form[key]) == 1
        identifier = form[key][0]
        assert identifier in IDENTIFIERS
        if self.mode == 'redirect':
            self.send_response(302)
            self.send_header('Location', '/must-not-follow')
            self.end_headers()
            return
        if self.mode == 'unavailable' and resource_type == 'Observation':
            self.send_json({'resourceType': 'OperationOutcome', 'issue': [{
                'severity': 'error', 'code': 'exception', 'diagnostics': 'must-not-leak-' + identifier}]}, 503)
            return
        patient = copy.deepcopy(PATIENT)
        if identifier == IDENTIFIERS[1]:
            patient['id'] = 'dhg-synthetic-b'
            patient['name'] = [{'text': 'Annen syntetisk testperson'}]
        if resource_type == 'Patient':
            self.send_json(bundle([patient]))
            return
        resources = [copy.deepcopy(e['resource']) for e in RESOURCES if e['resource']['resourceType'] == resource_type]
        if identifier == IDENTIFIERS[1] and resource_type != 'CareTeam':
            resources = []
        for resource in resources:
            resource['subject']['reference'] = 'Patient/' + patient['id']
        if self.mode == 'wrong-patient' and resources:
            resources[0]['subject']['reference'] = 'Patient/someone-else'
        self.send_json(bundle(resources))


def flat(items):
    return [entry for item in items for entry in [item, *flat(item.get('item', []))]]


def main():
    """Start isolerte prosesser, test appens DHG-flyt og stopp dem også når en kontroll feiler."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app-dir', type=Path, default=ROOT / 'publish')
    parser.add_argument('--browser', action='store_true', help='Also run browser-smoke.py; requires Playwright and a browser.')
    args = parser.parse_args()
    app_dir = args.app_dir.resolve()
    if not (app_dir / 'GenericPopulation.dll').exists():
        parser.error('Publish the app first (dotnet publish src/GenericPopulation -c Release -o publish).')
    count = 0

    def check(ok, name):
        nonlocal count
        assert ok, name
        count += 1
        print('PASS:', name)

    with ThreadingHTTPServer(('127.0.0.1', 0), DhgMock) as upstream, tempfile.TemporaryFile() as app_log, \
            tempfile.TemporaryDirectory(prefix='fhir-http-test-') as isolated:
        # Konfigurasjonstester skriver bare i denne kopien. Ingen lokale utviklerinnstillinger følger med.
        app_dir = Path(shutil.copytree(app_dir, Path(isolated) / 'app', ignore=shutil.ignore_patterns('.local')))
        thread = threading.Thread(target=upstream.serve_forever, daemon=True)
        thread.start()
        with socket.socket() as reserve:
            reserve.bind(('127.0.0.1', 0))
            app_port = reserve.getsockname()[1]
        base = f'http://127.0.0.1:{app_port}'
        env = dict(os.environ)
        # Overstyr lokale kildevalg også når utvikleren har satt egne Fhir-miljøvariabler.
        for key in list(env):
            if key.lower().startswith(('fhir__', 'demo__')):
                del env[key]
        env.update({
            'Demo__Port': str(app_port),
            'Fhir__Sources__0__BaseUrl': base + '/demo/fhir/',
            'Fhir__Sources__1__BaseUrl': f'http://127.0.0.1:{upstream.server_port}/fhir/',
            'Fhir__Sources__2__BaseUrl': base + '/demo/vitals/',
            'Fhir__QuestionnaireBindings__2__Questionnaire': 'urn:test:registered-dhg',
            'Fhir__QuestionnaireBindings__2__Version': '1.0.0',
            'Fhir__QuestionnaireBindings__2__ProfileId': 'dhg-test',
        })
        app = subprocess.Popen(['dotnet', 'GenericPopulation.dll'], cwd=app_dir, env=env,
                               stdout=app_log, stderr=subprocess.STDOUT)
        # Lokale testkall skal ikke gå gjennom maskinens eventuelle proxy.
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

        def call(path, body=None, raw=None, headers=None):
            data = raw if raw is not None else json.dumps(body).encode() if body is not None else None
            req = urllib.request.Request(base + path, data=data,
                                         headers={**({'Content-Type': 'application/json'} if data is not None else {}), **(headers or {})})
            try:
                response = opener.open(req, timeout=10)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                return response.status, json.loads(response.read()), response.headers

        try:
            for _ in range(60):
                if app.poll() is not None:
                    raise RuntimeError('Synthetic test app stopped during startup.')
                try:
                    if call('/health')[0] == 200:
                        break
                except (OSError, urllib.error.URLError):
                    pass
                time.sleep(0.2)
            else:
                raise RuntimeError('Synthetic test app did not become ready.')
            _, config, _ = call('/api/config')
            source = next(s for s in config['sources'] if s['id'] == 'dhg-test')
            check(source['patientInput'] == 'identifier' and source['testPatientIdentifiers'] == IDENTIFIERS,
                  'DHG source exposes only configured synthetic patient choices')
            check(not DhgMock.calls, 'No DHG requests on startup or config read')
            probe = {'configurationRevision': config['revision']}
            for mode, expected_state, expected_http in [('normal', 'ok', 200), ('status-auth', 'warning', 401),
                    ('status-not-fhir', 'warning', 200), ('status-not-r4', 'warning', 200),
                    ('status-missing', 'warning', 404), ('status-redirect', 'warning', 302), ('status-error', 'error', 503)]:
                DhgMock.mode = mode
                before_probe = len(DhgMock.calls)
                status, result, result_headers = call('/api/sources/dhg-test/status', probe)
                check(status == 200 and result['state'] == expected_state and result['httpStatus'] == expected_http
                      and result['checkedAt'] and result_headers['Cache-Control'] == 'no-store'
                      and DhgMock.calls[before_probe:] == [('GET', '/fhir/metadata', {})],
                      f'Endpoint check: {mode}, one anonymous metadata GET without redirects or patient data')
            before_probe = len(DhgMock.calls)
            for label, path, payload, custom_headers, expected in [
                ('stale revision', '/api/sources/dhg-test/status', {'configurationRevision': 'old'}, {}, 409),
                ('unknown source', '/api/sources/missing/status', probe, {}, 404),
                ('custom URL', '/api/sources/dhg-test/status', {**probe, 'url': 'https://other.example/'}, {}, 400),
                ('missing revision', '/api/sources/dhg-test/status', {}, {}, 400),
                ('wrong content type', '/api/sources/dhg-test/status', probe, {'Content-Type': 'text/plain'}, 415),
                ('cross-origin', '/api/sources/dhg-test/status', probe, {'Origin': 'https://other.example'}, 403),
                ('cross-site', '/api/sources/dhg-test/status', probe, {'Sec-Fetch-Site': 'cross-site'}, 403),
            ]:
                check(call(path, payload, headers=custom_headers)[0] == expected and len(DhgMock.calls) == before_probe,
                      f'Endpoint check rejects {label} before contacting source')
            duplicate_probe = json.dumps(probe)[:-1] + ', "configurationRevision": "other"}'
            check(call('/api/sources/dhg-test/status', raw=duplicate_probe.encode())[0] == 400
                  and len(DhgMock.calls) == before_probe, 'Endpoint check rejects duplicate revision')
            DhgMock.mode = 'normal'
            DhgMock.calls.clear()
            status, example, _ = call('/api/examples/dhg')
            check(status == 200 and example['id'] == 'dhg-demo', 'DHG example available in app')
            body = {'sourceId': 'dhg-test', 'patientIdentifier': IDENTIFIERS[0], 'questionnaire': QUESTIONNAIRE}
            status, data, headers = call('/api/populate', body)
            check(status == 200 and headers['X-Fhir-Requests'] == '4', 'Four real HTTP POST searches through app')
            qr = next(p['resource'] for p in data['parameter'] if p['name'] == 'response')
            items = {i['linkId']: i for i in flat(qr['item'])}
            check(qr['subject']['reference'] == 'Patient/dhg-synthetic-a' and IDENTIFIERS[0] not in json.dumps(qr), 'Pseudonym QR, no NIN copied')
            check(items['interpreterRequired']['answer'][0]['valueBoolean'] is False and 'answer' not in items['birthDate'], 'False retained, no inferred birth date')
            check(items['systolic']['answer'][0]['valueQuantity']['value'] == 128 and items['careTeamContacts']['answer'], 'Measurements and contained contacts')
            check(all(method == 'POST' and '?' not in path for method, path, _ in DhgMock.calls), 'No NIN in outbound URLs, no GET searches')
            status, direct, _ = call('/api/questionnaire-response', body)
            check(status == 200 and direct['resourceType'] == 'QuestionnaireResponse', 'DHG direct QR endpoint')
            status, data, _ = call('/api/populate', {**body, 'patientIdentifier': IDENTIFIERS[1]})
            assert status == 200, 'Second patient lookup failed: ' + str(status)
            qr_b = next(p['resource'] for p in data['parameter'] if p['name'] == 'response')
            items_b = {i['linkId']: i for i in flat(qr_b['item'])}
            check(status == 200 and qr_b['subject']['reference'] == 'Patient/dhg-synthetic-b' and 'answer' not in items_b['systolic'], 'Patient change cannot reuse previous observations')
            for label, changed, expected in [
                ('Unapproved NIN', {**body, 'patientIdentifier': '12345678901'}, 422),
                ('NIN stays a string', {**body, 'patientIdentifier': 29760484634}, 400),
                ('No logical ID in DHG input', {'sourceId': 'dhg-test', 'patientId': 'dhg-synthetic-a', 'questionnaire': QUESTIONNAIRE}, 400),
                ('No mixed identity fields', {**body, 'patientId': 'demo-patient'}, 400),
                ('No NIN input for generic source', {**body, 'sourceId': 'demo'}, 400),
            ]:
                before = len(DhgMock.calls)
                status, error, _ = call('/api/populate', changed)
                check(status == expected and error['resourceType'] == 'OperationOutcome' and len(DhgMock.calls) == before, label)
            duplicate = json.dumps(body)[:-1] + ', "sourceId": "demo"}'
            before = len(DhgMock.calls)
            check(call('/api/populate', raw=duplicate.encode())[0] == 400 and len(DhgMock.calls) == before, 'Duplicate fields rejected')
            for mode, code in [('unavailable', 'source-http'), ('wrong-patient', 'patient-mismatch'), ('redirect', 'source-http')]:
                DhgMock.mode = mode
                status, error, _ = call('/api/populate', body)
                check(status == 502 and error['resourceType'] == 'OperationOutcome' and error['issue'][0]['diagnostics'] == code,
                      'Controlled failure without partial QR: ' + mode)
                check('must-not-leak' not in json.dumps(error) and IDENTIFIERS[0] not in json.dumps(error), 'No upstream diagnostic or NIN in error: ' + mode)
            check(not any(method == 'GET' for method, _, _ in DhgMock.calls), 'Redirect was not followed')
            # En sentral Patient og to forskjellige kliniske endepunkter, uten DHG-kall.
            before = len(DhgMock.calls)
            multi_body = {'profileId': 'demo-multi', 'patientId': 'demo-patient',
                          'questionnaire': json.loads((ROOT / 'examples/questionnaire-pregnancy.json').read_text(encoding='utf-8'))}
            status, multi, headers = call('/api/populate', multi_body)
            check(status == 200 and headers['X-Fhir-Requests'] == '3', 'Multi-source profile with one central Patient lookup')
            stats = {next(part['valueString'] for part in p['part'] if part['name'] == 'id'):
                     next(part['valueInteger'] for part in p['part'] if part['name'] == 'requests')
                     for p in multi['parameter'] if p['name'] == 'source'}
            check(stats == {'demo': 2, 'demo-vitals': 1} and len(DhgMock.calls) == before,
                  'Configured routes use exactly the intended endpoints')
            response = next(p['resource'] for p in multi['parameter'] if p['name'] == 'response')
            items = {item['linkId']: item for item in flat(response['item'])}
            check(items['systolic']['answer'][0]['valueQuantity']['value'] == 128 and
                  items['gestationalAge']['answer'][0]['valueQuantity']['value'] == 210, 'Values from both endpoints reach QR')
            for invalid in ({**multi_body, 'sourceId': 'demo'}, {**multi_body, 'profileId': 'missing'},
                            {**multi_body, 'routes': []}):
                check(call('/api/populate', invalid)[0] == 400, 'Reject ambiguous or client-controlled profile selection')
            # Samme canonical, to versjoner. Profilvalg kan utelates, og manuelt valg kan ikke overstyre koblingen.
            for version, expected_profile, expected_sources in ((1, 'demo', {'demo'}), (2, 'demo-multi', {'demo', 'demo-vitals'})):
                q = json.loads((ROOT / f'examples/questionnaire-routed-v{version}.json').read_text(encoding='utf-8'))
                routed_body = {'patientId': 'demo-patient', 'questionnaire': q}
                status, routed, headers = call('/api/populate', routed_body)
                check(status == 200 and headers['X-Population-Profile'] == expected_profile,
                      f'Questionnaire version {version} selects configured profile without client choice')
                ids = {part['valueString'] for p in routed['parameter'] if p['name'] == 'source'
                       for part in p['part'] if part['name'] == 'id'}
                check(ids == expected_sources, f'Questionnaire version {version} uses correct endpoints')
            check(call('/api/populate', {**routed_body, 'profileId': 'demo'})[0] == 422, 'Reject profile override for registered version')
            check(call('/api/populate', {**routed_body, 'sourceId': 'demo'})[0] == 422, 'Legacy sourceId cannot bypass questionnaire binding')
            check(call('/api/populate', {**routed_body, 'profileId': 'demo-multi'})[0] == 200, 'Matching explicit profile remains compatible')
            status, direct, headers = call('/api/questionnaire-response', routed_body)
            check(status == 200 and direct['questionnaire'].endswith('|2.0.0') and headers['X-Population-Profile'] == 'demo-multi',
                  'Direct QR endpoint also resolves questionnaire version')
            registered_dhg = {**QUESTIONNAIRE, 'url': 'urn:test:registered-dhg'}
            before = len(DhgMock.calls)
            for q, extra in ((registered_dhg, {'profileId': 'demo'}),
                             ({**registered_dhg, 'version': 'unknown'}, {})):
                status, error, _ = call('/api/populate', {'questionnaire': q, 'patientIdentifier': IDENTIFIERS[0], **extra})
                check(status == 422 and error['issue'][0]['diagnostics'] == 'questionnaire-routing' and len(DhgMock.calls) == before,
                      'Unknown version or conflicting profile rejected before central Patient lookup')
            # Redigerbart oppsett er ett atomisk dokument; validering, konflikter og opprinnelse kontrolleres i HTTP-grensen.
            before = len(DhgMock.calls)
            status, original, config_headers = call('/api/configuration')
            check(status == 200 and config_headers['Cache-Control'] == 'no-store', 'Configuration is readable without browser caching')
            check(original['revision'] == config['revision'] and original['configuration']['Sources'][0]['Id'] == 'demo',
                  'Configuration editor and population page share a revision')
            update = {'revision': original['revision'], 'configuration': copy.deepcopy(original['configuration'])}
            update['configuration']['Sources'][0]['Name'] = 'Updated local demo'
            check(call('/api/configuration/validate', update)[0] == 200 and not (app_dir / '.local').exists(),
                  'Draft validation does not persist or activate changes')
            check(call('/api/configuration')[1] == original, 'Validation retains original snapshot')
            invalid = copy.deepcopy(update)
            invalid['configuration']['QuestionnaireBindings'][0]['ProfileId'] = 'missing-profile'
            check(call('/api/configuration', invalid)[0] == 422 and call('/api/configuration')[1] == original,
                  'Invalid references cannot be saved or activated')
            check(call('/api/configuration', update, headers={'Origin': 'https://untrusted.example'})[0] == 403,
                  'Cross-origin configuration edits are rejected')
            check(call('/api/configuration', update, headers={'Sec-Fetch-Site': 'cross-site'})[0] == 403,
                  'Cross-site configuration edits are rejected without Origin too')
            check(call('/api/configuration', update, headers={'Content-Type': 'text/plain'})[0] == 415,
                  'Configuration edits require JSON content type')
            check(call('/api/configuration', {**update, 'path': 'elsewhere.json'})[0] == 400,
                  'Clients cannot choose configuration storage paths')
            status, saved, _ = call('/api/configuration', update)
            check(status == 200 and saved['revision'] != original['revision'], 'Save returns a new revision')
            persisted = json.loads((app_dir / '.local/fhir-configuration.json').read_text(encoding='utf-8'))
            check(persisted == saved['configuration'] and persisted['Sources'][0]['Name'] == 'Updated local demo',
                  'Saved file matches active configuration')
            check(call('/api/config')[1]['profiles'][0]['name'] == 'Updated local demo', 'New settings reach population page immediately')
            check(call('/api/configuration', update)[0] == 409 and call('/api/configuration')[1] == saved,
                  'Stale browser cannot overwrite saved settings')
            check(call('/api/populate', {**body, 'configurationRevision': original['revision']})[0] == 409 and len(DhgMock.calls) == before,
                  'Stale population settings are rejected before any Patient lookup')
            check(call('/api/populate', {**multi_body, 'configurationRevision': saved['revision']})[0] == 200,
                  'Population with current revision works')
            # Tilbakestill før eksisterende GUI-regresjoner. Kopien fjernes når prosessen har stoppet.
            check(call('/api/configuration', {'revision': saved['revision'], 'configuration': original['configuration']})[0] == 200,
                  'Previous settings can be restored explicitly')
            if args.browser:
                DhgMock.mode = 'normal'
                browser_env = dict(env, FHIR_TEST_BASE_URL=base, FHIR_TEST_DHG_MOCK='true')
                for script in ('browser-smoke.py', 'browser-state-tests.py', 'browser-configuration-tests.py'):
                    subprocess.run([sys.executable, '-X', 'utf8', str(ROOT / 'verification' / script)],
                                   cwd=ROOT, env=browser_env, check=True, timeout=120)
        finally:
            app.terminate()
            try:
                app.wait(timeout=10)
            except subprocess.TimeoutExpired:
                app.kill()
                app.wait(timeout=5)
            upstream.shutdown()
            thread.join(timeout=5)
        app_log.seek(0)
        logs = app_log.read().decode('utf-8', errors='replace')
        check(all(value not in logs for value in IDENTIFIERS + ['Syntetisk DHG-eksempel', 'must-not-leak']), 'No identifiers or clinical payloads in app logs')
    print(f'{count}/{count} DHG HTTP checks passed. No external services contacted.')


if __name__ == '__main__':
    main()
