"""GUI-regresjoner for konfigurasjon. Kjøres sist i dhg-http-smoke sin isolerte appkopi.

Oppretter kun lokale syntetiske endepunkter. Kontrollerer virkelig lagring og populering,
JSON-feil, referanser, samtidige faner, eksport og mobilvisning.
"""
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
BASE = os.environ.get('FHIR_TEST_BASE_URL', 'http://127.0.0.1:5077')
SCREENSHOTS = Path(os.environ.get('FHIR_SCREENSHOT_DIR', ROOT / 'output/browser'))


async def main():
    count = 0

    def passed(name):
        nonlocal count
        count += 1
        print('PASS:', name)

    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True, channel=os.environ.get('PLAYWRIGHT_CHANNEL') or None)
        context = await browser.new_context(viewport={'width': 1440, 'height': 1000})
        errors = []
        context.on('page', lambda page: page.on('pageerror', lambda error: errors.append(str(error))))
        original = await (await context.request.get(BASE + '/api/configuration')).json()
        try:
            app = await context.new_page()
            await app.goto(BASE)
            await expect(app.locator('#populate')).to_be_enabled()
            await expect(app.get_by_role('link', name='Konfigurasjon', exact=True)).to_be_visible()
            page = await context.new_page()
            await page.goto(BASE + '/configuration.html')
            await expect(page.locator('#cfg-Id')).to_have_value('demo')
            await expect(page.locator('#config-save')).to_be_enabled()
            stale = await context.new_page()
            await stale.goto(BASE + '/configuration.html')
            await expect(stale.locator('#cfg-Id')).to_have_value('demo')
            passed('Configuration page loads current settings and is linked from population page')

            # Uferdig JSON må bevares ved navigasjon og aldri føre til en serverendring.
            capabilities = await page.locator('#cfg-Capabilities').input_value()
            await page.locator('#cfg-Capabilities').fill('{invalid')
            await page.locator('#config-save').click()
            await expect(page.locator('#config-message')).to_contain_text('ugyldig JSON')
            await page.locator('[data-kind=PopulationProfiles]').click()
            await expect(page.locator('#cfg-Capabilities')).to_have_value('{invalid')
            assert (await (await context.request.get(BASE + '/api/configuration')).json()) == original
            await page.locator('#cfg-Capabilities').fill(capabilities)
            passed('Invalid JSON stays in editor and blocks both save and selection changes')

            await page.locator('#config-add').click()
            await page.locator('#cfg-Id').fill('gui-vitals')
            await page.locator('#cfg-Name').fill('GUI målinger')
            await page.locator('#cfg-BaseUrl').fill(BASE + '/demo/vitals/')
            await page.locator('#cfg-ExposeAsProfile').uncheck()
            await page.locator('[data-kind=PopulationProfiles]').click()
            await page.locator('#config-add').click()
            await page.locator('#cfg-Id').fill('gui-profile')
            await page.locator('#cfg-Name').fill('GUI profil')
            await page.locator('#cfg-PatientSource').select_option('demo')
            await page.locator('#cfg-DefaultSource').select_option('demo')
            await page.locator('#cfg-Routes').fill(json.dumps([
                {'ResourceType': 'Observation', 'Code': 'http://loinc.org|85354-9', 'Source': 'gui-vitals'}]))
            await page.locator('[data-kind=QuestionnaireBindings]').click()
            await page.locator('#config-add').click()
            await page.locator('#cfg-Questionnaire').fill('urn:test:configuration-browser')
            await page.locator('#cfg-Version').fill('3.0')
            await page.locator('#cfg-ProfileId').select_option('gui-profile')
            await page.locator('#config-validate').click()
            await expect(page.locator('#config-message')).to_contain_text('Utkastet er gyldig')
            await expect(page.locator('#config-state')).to_have_text('Ulagrede endringer')
            assert (await (await context.request.get(BASE + '/api/configuration')).json()) == original
            await page.locator('#config-save').click()
            await expect(page.locator('#config-message')).to_contain_text('lagret og aktiv')
            await expect(page.locator('#config-state')).to_have_text('Lagret')
            saved = await (await context.request.get(BASE + '/api/configuration')).json()
            assert saved['revision'] != original['revision']
            assert any(s['Id'] == 'gui-vitals' for s in saved['configuration']['Sources'])
            passed('New source, profile and exact questionnaire version validate without saving, then activate together')

            # En åpen preutfyllingsfane må ikke bruke endepunkter endret av en annen fane i stillhet.
            await app.locator('#populate').click()
            await expect(app.locator('#message')).to_contain_text('Konfigurasjonen er endret')
            await expect(app.locator('#output-result')).to_be_hidden()
            await app.reload()
            await expect(app.locator('#populate')).to_be_enabled()
            q = json.loads((ROOT / 'examples/questionnaire-pregnancy.json').read_text(encoding='utf-8'))
            q.update(url='urn:test:configuration-browser', version='3.0')
            await app.locator('#questionnaire').fill(json.dumps(q))
            await expect(app.locator('#source')).to_have_value('gui-profile')
            await expect(app.locator('#source')).to_be_disabled()
            async with app.expect_response('**/api/populate') as result:
                await app.locator('#populate').click()
            response = await result.value
            assert response.status == 200
            payload = await response.json()
            source_ids = {part['valueString'] for p in payload['parameter'] if p['name'] == 'source'
                          for part in p['part'] if part['name'] == 'id'}
            assert source_ids == {'demo', 'gui-vitals'}
            await expect(app.locator('#output-result')).to_be_visible()
            passed('Saved questionnaire binding populates from both configured endpoints; stale population page must reload')

            await stale.locator('#cfg-Name').fill('Unsaved stale draft')
            await stale.locator('#config-save').click()
            await expect(stale.locator('#config-message')).to_contain_text('Konfigurasjonen er endret')
            await expect(stale.locator('#cfg-Name')).to_have_value('Unsaved stale draft')
            assert (await (await context.request.get(BASE + '/api/configuration')).json()) == saved
            stale.once('dialog', lambda dialog: dialog.accept())
            await stale.locator('#config-reload').click()
            await expect(stale.locator('#config-state')).to_have_text('Lagret')
            await expect(stale.locator('#cfg-Name')).not_to_have_value('Unsaved stale draft')
            passed('Conflicting save preserves the draft and cannot overwrite another tab; explicit reload recovers')

            await page.locator('[data-kind=Sources]').click()
            await page.locator('#config-list .config-entry').filter(has_text='gui-vitals').click()
            await page.locator('#config-remove').click()
            await page.locator('#config-save').click()
            await expect(page.locator('#config-message')).to_have_class('message error')
            assert (await (await context.request.get(BASE + '/api/configuration')).json()) == saved
            page.once('dialog', lambda dialog: dialog.accept())
            await page.locator('#config-reload').click()
            await expect(page.locator('#config-state')).to_have_text('Lagret')
            passed('Deleting a referenced source is rejected without changing active settings')

            await page.locator('[data-kind=QuestionnaireBindings]').click()
            await page.locator('#config-list .config-entry').filter(has_text='urn:test:configuration-browser').click()
            await page.locator('#config-duplicate').click()
            await expect(page.locator('#cfg-Version')).to_have_value('')
            await page.locator('#cfg-Version').fill('3.1')
            await page.locator('#config-require-binding').check()
            await page.locator('#config-save').click()
            await expect(page.locator('#config-state')).to_have_text('Lagret')
            latest = await (await context.request.get(BASE + '/api/configuration')).json()
            assert latest['configuration']['RequireQuestionnaireBinding'] is True
            assert {b['Version'] for b in latest['configuration']['QuestionnaireBindings']
                    if b['Questionnaire'] == q['url']} == {'3.0', '3.1'}
            async with page.expect_download() as download_info:
                await page.locator('#config-export').click()
            download = await download_info.value
            downloaded = json.loads(Path(await download.path()).read_text(encoding='utf-8'))
            assert downloaded == latest['configuration']
            passed('Duplicate version, strict binding switch and JSON export preserve the complete configuration')

            SCREENSHOTS.mkdir(parents=True, exist_ok=True)
            await page.screenshot(path=str(SCREENSHOTS / 'configuration-desktop.png'), full_page=True)
            await page.set_viewport_size({'width': 390, 'height': 844})
            await page.locator('[data-kind=Sources]').click()
            await expect(page.locator('#cfg-Id')).to_be_visible()
            assert await page.evaluate('document.documentElement.scrollWidth <= innerWidth')
            await page.screenshot(path=str(SCREENSHOTS / 'configuration-mobile.png'), full_page=True)
            passed('Configuration editor remains usable without horizontal overflow on a narrow mobile screen')

            failed = await context.new_page()
            await failed.route('**/api/configuration', lambda route: route.fulfill(status=503, content_type='application/json', body='{}'))
            await failed.goto(BASE + '/configuration.html')
            await expect(failed.locator('#config-message')).to_contain_text('503')
            await expect(failed.locator('#config-save')).to_be_disabled()
            await expect(failed.locator('#config-add')).to_be_disabled()
            await expect(failed.locator('#config-reload')).to_be_enabled()
            assert not errors, errors
            passed('Failed initial load leaves editing disabled with a retry action; no JavaScript errors')
        finally:
            # Behold testmiljøets utgangspunkt også ved feil; brukerens app kjøres aldri av denne testen.
            current = await (await context.request.get(BASE + '/api/configuration')).json()
            restored = await context.request.post(BASE + '/api/configuration', data={
                'revision': current['revision'], 'configuration': original['configuration']})
            assert restored.ok
            await context.close()
            await browser.close()
    print(f'{count}/{count} configuration UI regressions passed.')


if __name__ == '__main__':
    asyncio.run(main())
