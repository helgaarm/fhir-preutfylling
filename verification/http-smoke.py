"""HTTP regression tests. Start the app first. Standard-library Python only."""
import copy
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

BASE = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5077'
ROOT = Path(__file__).resolve().parents[1]
COUNT = 0

def call(path, body=None, headers=None, raw=None):
    data = raw if raw is not None else json.dumps(body).encode() if body is not None else None
    h = {'Content-Type': 'application/json'} if data is not None else {}
    h.update(headers or {})
    req = urllib.request.Request(BASE + path, data=data, headers=h)
    try:
        response = urllib.request.urlopen(req, timeout=65)
    except urllib.error.HTTPError as error:
        response = error
    content = response.read()
    return response.status, json.loads(content), response.headers

def check(ok, name):
    global COUNT
    assert ok, name
    COUNT += 1
    print('PASS:', name)

def flat(items):
    return [entry for item in items for entry in [item, *flat(item.get('item', []))]]

q = json.loads((ROOT / 'examples/questionnaire-pregnancy.json').read_text())
body = {'sourceId': 'demo', 'patientId': 'demo-patient', 'questionnaire': q}
status, data, headers = call('/api/populate', body)
check(status == 200 and data['resourceType'] == 'Parameters', 'Parameters envelope')
qr = next(p['resource'] for p in data['parameter'] if p['name'] == 'response')
items = {i['linkId']: i for i in flat(qr['item'])}
check(qr['status'] == 'in-progress' and qr['questionnaire'] == q['url'] + '|1.0.0', 'Version and status')
check(items['gestationalAge']['answer'][0]['valueQuantity']['value'] == 210, 'Gestational age 210 days')
check(items['systolic']['answer'][0]['valueQuantity']['value'] == 128 and items['diastolic']['answer'][0]['valueQuantity']['value'] == 76, 'Blood pressure 128/76')
check('answer' not in items['comment'], 'Missing answer stays empty')
check(headers['X-Fhir-Requests'] == '3', 'Patient and two Observation searches via HTTP')
check(headers['Cache-Control'] == 'no-store', 'No response caching')
(ROOT / 'verification/actual-response-pregnancy.json').write_text(json.dumps(qr, indent=2, ensure_ascii=False) + '\n')
status, direct, _ = call('/api/questionnaire-response', body)
check(status == 200 and direct['resourceType'] == 'QuestionnaireResponse', 'Direct QR endpoint')
check(direct['id'] != qr['id'], 'A new QR is created per request')
general = json.loads((ROOT / 'examples/questionnaire-general.json').read_text())
status, data, headers = call('/api/populate', {**body, 'questionnaire': general})
qr = next(p['resource'] for p in data['parameter'] if p['name'] == 'response')
items = {i['linkId']: i for i in flat(qr['item'])}
check(items['recordActive']['answer'][0]['valueBoolean'] is False and len(items['given']['answer']) == 2 and headers['X-Fhir-Requests'] == '1', 'False and repeated names via Patient HTTP')
for name, changed, expected in [
    ('Unknown source', {**body, 'sourceId': 'missing'}, 400),
    ('Unknown patient', {**body, 'patientId': 'missing'}, 502),
    ('Invalid patient ID', {**body, 'patientId': '../other'}, 422),
    ('Existing QR rejected', {**body, 'response': qr}, 400),
    ('Client authorization flag rejected', {**body, 'prepopulationAllowed': True}, 400),
    ('QuestionnaireResponse as Q rejected', {**body, 'questionnaire': qr}, 422),
    ('Missing Q', {'sourceId': 'demo', 'patientId': 'demo-patient'}, 400),
]:
    status, data, _ = call('/api/populate', changed)
    check(status == expected and data['resourceType'] == 'OperationOutcome', name)
invalid = copy.deepcopy(q)
invalid.pop('version')
status, data, _ = call('/api/populate', {**body, 'questionnaire': invalid})
check(status == 422 and data['issue'][0]['diagnostics'] == 'questionnaire-version', 'Missing version rejected')
status, data, _ = call('/api/populate', raw=b'{broken')
check(status == 400 and data['resourceType'] == 'OperationOutcome', 'Malformed JSON')
status, data, _ = call('/api/populate', body, {'Origin': 'https://unrelated.example'})
check(status == 403, 'Cross-origin POST blocked')
status, data, _ = call('/api/populate', body, {'Content-Type': 'text/plain'})
check(status == 415, 'Non-JSON input blocked')
status, data, _ = call('/api/populate', raw=json.dumps({**body, 'padding': 'x' * 270000}).encode())
check(status == 413, 'Request size limit')
print(f'{COUNT}/{COUNT} HTTP checks passed.')
