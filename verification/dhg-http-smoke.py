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
        self.send_json({'resourceType': 'OperationOutcome'}, 405)

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

    with ThreadingHTTPServer(('127.0.0.1', 0), DhgMock) as upstream, tempfile.TemporaryFile() as app_log:
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
        })
        app = subprocess.Popen(['dotnet', 'GenericPopulation.dll'], cwd=app_dir, env=env,
                               stdout=app_log, stderr=subprocess.STDOUT)
        # Lokale testkall skal ikke gå gjennom maskinens eventuelle proxy.
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

        def call(path, body=None, raw=None):
            data = raw if raw is not None else json.dumps(body).encode() if body is not None else None
            req = urllib.request.Request(base + path, data=data,
                                         headers={'Content-Type': 'application/json'} if data is not None else {})
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
            if args.browser:
                DhgMock.mode = 'normal'
                browser_env = dict(env, FHIR_TEST_BASE_URL=base, FHIR_TEST_DHG_MOCK='true')
                subprocess.run([sys.executable, '-X', 'utf8', str(ROOT / 'verification/browser-smoke.py')],
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
