"""Eget skjema og direkte pasientinput uten registrering, mot lokal DHG-testdobbel."""
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
BASE = os.environ.get('FHIR_TEST_BASE_URL', '')
SCREENSHOTS = Path(os.environ.get('FHIR_SCREENSHOT_DIR', ROOT / 'output/browser'))


async def main():
    assert BASE and os.environ.get('FHIR_TEST_DHG_MOCK') == 'true', 'Run through dhg-http-smoke.py --browser'
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True, channel=os.environ.get('PLAYWRIGHT_CHANNEL') or None)
        context = await browser.new_context(viewport={'width': 1440, 'height': 1100})
        original = await (await context.request.get(BASE + '/api/configuration')).json()
        errors, calls, configuration_calls = [], [], []
        try:
            page = await context.new_page()
            page.on('pageerror', lambda error: errors.append(str(error)))
            page.on('request', lambda request: calls.append(request) if request.url.endswith('/api/populate') else None)
            page.on('request', lambda request: configuration_calls.append(request) if '/api/configuration' in request.url else None)
            await page.goto(BASE)
            await expect(page.locator('#populate')).to_be_enabled()
            questionnaire = json.loads((ROOT / 'examples/questionnaire-dhg.json').read_text(encoding='utf-8'))
            questionnaire['url'] = 'urn:test:uploaded-manual-questionnaire'
            async with page.expect_file_chooser() as chooser:
                await page.locator('#upload').click()
            await (await chooser.value).set_files({'name': 'my-questionnaire.json', 'mimeType': 'application/json',
                                                 'buffer': json.dumps(questionnaire).encode()})
            await expect(page.locator('#example')).to_have_value('custom')
            await page.locator('#source').select_option('dhg-test')
            assert json.loads(await page.locator('#questionnaire').input_value()) == questionnaire
            await expect(page.get_by_label('Testpersonens fødselsnummer / identifikator', exact=True)).to_be_visible()
            await expect(page.locator('#test-patient option[value="00000000001"]')).to_have_count(0)
            await page.locator('#patient').fill(' 00000000001 ')
            await page.locator('#populate').click()
            await expect(page.locator('#preview')).to_contain_text('Manuelt registrert testperson')
            assert calls[-1].post_data_json['patientIdentifier'] == '00000000001'
            assert 'patientId' not in calls[-1].post_data_json
            assert calls[-1].post_data_json['configurationRevision'] == original['revision']
            response = json.loads(await page.locator('#qr-json').text_content())
            assert response['subject']['reference'] == 'Patient/dhg-synthetic-c'
            assert '00000000001' not in json.dumps(response)
            assert not configuration_calls, 'Home page must not call the configuration editor API'
            assert await (await context.request.get(BASE + '/api/configuration')).json() == original
            print('PASS: Upload own Q, type unlisted testperson directly and populate without configuration changes.')

            await page.locator('#patient').fill('')
            await expect(page.locator('#output-result')).to_be_hidden()
            before = len(calls)
            await page.locator('#populate').click()
            await expect(page.locator('#message')).to_contain_text('Skriv inn testpersonens')
            assert len(calls) == before
            await page.locator('#patient').fill('invalid')
            await page.locator('#populate').click()
            await expect(page.locator('#message')).to_contain_text('gyldig format')
            await expect(page.locator('#patient')).to_have_value('invalid')
            await expect(page.locator('#output-result')).to_be_hidden()
            print('PASS: Empty and malformed identifiers give clear errors and preserve input.')

            await page.locator('#patient').fill('11859699482')
            await page.locator('#populate').click()
            await expect(page.locator('#preview')).to_contain_text('Annen syntetisk testperson')
            await expect(page.locator('#preview')).not_to_contain_text('Manuelt registrert testperson')
            await page.locator('#patient').fill('00000000001')
            await page.locator('#source').select_option('demo')
            await expect(page.locator('#patient')).to_have_value('demo-patient')
            await page.locator('#source').select_option('dhg-test')
            await expect(page.locator('#patient')).to_have_value('00000000001')
            assert json.loads(await page.locator('#questionnaire').input_value()) == questionnaire
            await expect(page.locator('#output-result')).to_be_hidden()
            SCREENSHOTS.mkdir(parents=True, exist_ok=True)
            await page.screenshot(path=str(SCREENSHOTS / 'manual-input-desktop.png'), full_page=True)
            await page.set_viewport_size({'width': 390, 'height': 844})
            await page.screenshot(path=str(SCREENSHOTS / 'manual-input-mobile.png'), full_page=True)
            assert await page.evaluate('document.documentElement.scrollWidth <= window.innerWidth')
            await page.locator('#populate').click()
            await expect(page.locator('#preview')).to_contain_text('Manuelt registrert testperson')
            print('PASS: Changing patients clears old results; custom Q and input survive profile switching, also on mobile.')

            await page.reload()
            await expect(page.locator('#populate')).to_be_enabled()
            await page.locator('#source').select_option('dhg-test')
            await expect(page.locator('#patient')).to_have_value('29760484634')
            await expect(page.locator('#test-patient option[value="00000000001"]')).to_have_count(0)
            assert await (await context.request.get(BASE + '/api/configuration')).json() == original
            assert not configuration_calls
            assert await page.evaluate('localStorage.length') == 0
            assert not errors, errors
            print('PASS: Manual identifiers are not registered, persisted in configuration or stored in the browser.')
        finally:
            await browser.close()


if __name__ == '__main__':
    asyncio.run(main())
