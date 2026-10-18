"""Pasientinitialisering og profilbytte med deterministisk forsinket konfigurasjon.

Kjøres via dhg-http-smoke.py --browser mot lokal demo og syntetisk DHG-testdobbel.
Bare konfigurasjonens profilrekkefølge/forslag endres i nettleseren; preutfylling
går gjennom det virkelige API-et. Ingen konfigurasjon lagres eller eksterne kall gjøres.
"""
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
BASE = os.environ.get('FHIR_TEST_BASE_URL', '')
DHG_PATIENT = '29760484634'
MANUAL_PATIENT = '00000000001'


async def startup(page, profile, expected_patient, *, edits=(), custom=False,
                  no_suggestions=False, default_patient=None, populate=True):
    ready, release = asyncio.Event(), asyncio.Event()
    calls = []
    page.on('request', lambda request: calls.append(request) if request.url.endswith('/api/populate') else None)

    async def delayed_config(route):
        response = await route.fetch()
        config = await response.json()
        # DHG blir eneste profil, som når demokilden og dens profiler er fjernet.
        config['profiles'] = [p for p in config['profiles'] if p['id'] == profile]
        assert len(config['profiles']) == 1
        if no_suggestions:
            config['profiles'][0]['testPatientIdentifiers'] = []
        if default_patient is not None:
            config['defaultPatientId'] = default_patient
        ready.set()
        await release.wait()
        await route.fulfill(response=response, json=config)

    await page.route('**/api/config', delayed_config)
    await page.goto(BASE, wait_until='domcontentloaded')
    await asyncio.wait_for(ready.wait(), timeout=10)
    await expect(page.locator('#patient')).to_have_value('demo-patient')
    for value in edits:
        await page.locator('#patient').fill(value)
    questionnaire = json.loads((ROOT / 'examples/questionnaire-dhg.json').read_text(encoding='utf-8'))
    if custom:
        await page.locator('#questionnaire').fill(json.dumps(questionnaire))
    await expect(page.locator('#populate')).to_be_disabled()
    await page.locator('#patient').press('Control+Enter')
    assert not calls, 'No population before configuration is available'
    release.set()

    await expect(page.locator('#populate')).to_be_enabled()
    await expect(page.locator('#source')).to_have_value(profile)
    await expect(page.locator('#patient')).to_have_value(expected_patient)
    if custom:
        await expect(page.locator('#example')).to_have_value('custom')
        assert json.loads(await page.locator('#questionnaire').input_value()) == questionnaire
    if no_suggestions:
        await expect(page.locator('#test-patient option')).to_have_count(0)
    assert not calls, 'Startup must not populate automatically'
    if not expected_patient:
        await page.locator('#populate').click()
        await expect(page.locator('#message')).to_contain_text('Skriv inn')
        assert not calls, 'A deliberately empty field must not trigger population'
    elif populate:
        await page.locator('#populate').click()
        await expect(page.locator('#output-result')).to_be_visible()
        assert len(calls) == 1
        body = calls[0].post_data_json
        key, other = ('patientIdentifier', 'patientId') if profile == 'dhg-test' else ('patientId', 'patientIdentifier')
        assert body[key] == expected_patient and other not in body
        assert body['profileId'] == profile
        qr = json.loads(await page.locator('#qr-json').text_content())
        expected_id = ('dhg-synthetic-c' if expected_patient == MANUAL_PATIENT else 'dhg-synthetic-a') if profile == 'dhg-test' else 'demo-patient'
        assert qr['subject']['reference'] == 'Patient/' + expected_id


async def profile_switches(page):
    await page.goto(BASE)
    await expect(page.locator('#populate')).to_be_enabled()
    await page.locator('#populate').click()
    await expect(page.locator('#output-result')).to_be_visible()
    await page.locator('#patient').fill('manual-demo-id')
    await expect(page.locator('#output-result')).to_be_hidden()

    async def select(profile, patient):
        await page.locator('#source').select_option(profile)
        await expect(page.locator('#populate')).to_be_enabled()
        await expect(page.locator('#patient')).to_have_value(patient)
        await expect(page.locator('#output-result')).to_be_hidden()

    # Profiler med samme pasientkilde deler input. En ny kilde får sitt eget forslag
    # selv om brukeren tidligere har redigert feltet for en annen kilde.
    await select('demo-multi', 'manual-demo-id')
    await select('dhg-test', DHG_PATIENT)
    await page.locator('#patient').fill(MANUAL_PATIENT)
    await select('demo', 'manual-demo-id')
    await select('dhg-test', MANUAL_PATIENT)
    await page.locator('#patient').fill('')
    await select('demo-multi', 'manual-demo-id')
    await select('dhg-test', '')
    await page.locator('#patient').fill(MANUAL_PATIENT)
    await page.locator('#populate').click()
    await expect(page.locator('#preview')).to_contain_text('Manuelt registrert testperson')


async def main():
    assert BASE and os.environ.get('FHIR_TEST_DHG_MOCK') == 'true', 'Run through dhg-http-smoke.py --browser'
    scenarios = [
        ('Demo initializes and populates with patientId', 'demo', 'demo-patient', {}),
        ('DHG first initializes its suggestion and populates with patientIdentifier', 'dhg-test', DHG_PATIENT, {}),
        ('Untouched demo uses the configured default', 'demo', 'configured-demo',
         {'default_patient': 'configured-demo', 'populate': False}),
        ('Early manual DHG identifier survives and populates', 'dhg-test', MANUAL_PATIENT,
         {'edits': (MANUAL_PATIENT,)}),
        ('Early manual demo ID survives', 'demo', 'manual-demo-id',
         {'edits': ('manual-demo-id',), 'populate': False}),
        ('Deliberately cleared DHG input stays empty', 'dhg-test', '', {'edits': ('',)}),
        ('Deliberately cleared demo input stays empty', 'demo', '', {'edits': ('',)}),
        ('Editing back to the HTML default still counts as manual input', 'dhg-test', 'demo-patient',
         {'edits': ('edited', 'demo-patient'), 'populate': False}),
        ('DHG without suggestions starts empty', 'dhg-test', '', {'no_suggestions': True}),
        ('DHG without suggestions preserves manual input', 'dhg-test', MANUAL_PATIENT,
         {'edits': (MANUAL_PATIENT,), 'no_suggestions': True}),
        ('Early questionnaire editing does not suppress patient initialization', 'dhg-test', DHG_PATIENT,
         {'custom': True}),
    ]
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True, channel=os.environ.get('PLAYWRIGHT_CHANNEL') or None)
        try:
            for name, profile, expected, options in scenarios:
                context = await browser.new_context()
                try:
                    page = await context.new_page()
                    errors = []
                    page.on('pageerror', lambda error: errors.append(str(error)))
                    await startup(page, profile, expected, **options)
                    assert not errors, errors
                    print('PASS:', name)
                finally:
                    await context.close()
            context = await browser.new_context()
            try:
                page = await context.new_page()
                errors = []
                page.on('pageerror', lambda error: errors.append(str(error)))
                await profile_switches(page)
                assert not errors, errors
                print('PASS: Profile switches preserve per-source input, including empty values')
            finally:
                await context.close()
        finally:
            await browser.close()
    print(f'{len(scenarios) + 1}/{len(scenarios) + 1} patient startup/profile regressions passed.')


if __name__ == '__main__':
    asyncio.run(main())
