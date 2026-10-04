"""Regresjoner for asynkron UI-tilstand med kontrollerte forsinkelser og lokal demokilde.

Kjøres mot samme lokale app som browser-smoke.py. Ingen eksterne FHIR-kall.
"""
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
BASE = os.environ.get('FHIR_TEST_BASE_URL', 'http://127.0.0.1:5077')
GENERAL = (ROOT / 'examples/questionnaire-general.json').read_text(encoding='utf-8')
CANONICAL = json.loads(GENERAL)['url'] + '|' + json.loads(GENERAL)['version']
CONTROLS = ('source', 'example', 'upload', 'populate')


async def result_matches_editor(page):
    await expect(page.locator('#output-result')).to_be_visible()
    response = json.loads(await page.locator('#qr-json').text_content())
    assert response['questionnaire'] == CANONICAL
    with_page = json.loads(await page.locator('#questionnaire').input_value())
    assert with_page['url'] + '|' + with_page['version'] == CANONICAL


async def early_input(page):
    release = asyncio.Event()

    async def delayed_config(route):
        response = await route.fetch()
        await release.wait()
        await route.fulfill(response=response)

    calls = []
    page.on('request', lambda request: calls.append(request.url) if request.url.endswith('/api/populate') else None)
    await page.route('**/api/config', delayed_config)
    await page.goto(BASE, wait_until='domcontentloaded')
    await page.locator('#questionnaire').fill(GENERAL)
    await page.locator('#questionnaire').press('Control+Enter')
    assert not calls
    release.set()
    for control in CONTROLS:
        await expect(page.locator('#' + control)).to_be_enabled()
    await expect(page.locator('#example')).to_have_value('custom')
    assert json.loads(await page.locator('#questionnaire').input_value()) == json.loads(GENERAL)
    await page.locator('#populate').click()
    await result_matches_editor(page)


async def config_failure(page):
    await page.route('**/api/config', lambda route: route.fulfill(status=503, body='Unavailable'))
    calls = []
    page.on('request', lambda request: calls.append(request.url) if request.url.endswith('/api/populate') else None)
    await page.goto(BASE)
    await expect(page.locator('#message')).to_contain_text('Kunne ikke starte appen')
    for control in CONTROLS:
        await expect(page.locator('#' + control)).to_be_disabled()
    await expect(page.locator('#questionnaire')).to_be_enabled()
    await page.locator('#questionnaire').fill(GENERAL)
    await page.locator('#questionnaire').press('Control+Enter')
    assert not calls


async def pending_file(page, outcome):
    # Forsink kun fil-lesing; appens eventhandlere, nettverkskall og rendering kjøres uendret.
    await page.add_init_script("""(() => {
        const read = File.prototype.text;
        File.prototype.text = function () {
            return new Promise((resolve, reject) => {
                window.finishFileRead = () => read.call(this).then(resolve, reject);
                window.failFileRead = () => reject(new Error('Synthetic read failure'));
            });
        };
    })();""")
    await page.goto(BASE)
    await expect(page.locator('#populate')).to_be_enabled()
    await page.locator('#populate').click()
    await expect(page.locator('#output-result')).to_be_visible()
    calls = []
    page.on('request', lambda request: calls.append(request.url) if request.url.endswith('/api/populate') else None)
    payload = GENERAL if outcome != 'invalid-json' else '{invalid'
    await page.locator('#file').set_input_files({'name': 'questionnaire.json', 'mimeType': 'application/json', 'buffer': payload.encode()})
    for control in (*CONTROLS, 'questionnaire', 'file', 'patient'):
        await expect(page.locator('#' + control)).to_be_disabled()
    await expect(page.locator('#output-result')).to_be_hidden()
    await page.keyboard.press('Control+Enter')
    assert not calls, 'No population while the new questionnaire is still being read'
    await page.evaluate('window.failFileRead()' if outcome == 'read-error' else 'window.finishFileRead()')
    await expect(page.locator('#populate')).to_be_enabled()
    await expect(page.locator('#file')).to_have_value('')
    await expect(page.locator('#output-result')).to_be_hidden()
    if outcome == 'success':
        await expect(page.locator('#example')).to_have_value('custom')
        await page.locator('#populate').click()
        await result_matches_editor(page)
    else:
        await expect(page.locator('#message')).to_be_visible()
        # En mislykket lesing må frigjøre kontrollene slik at neste opplasting kan lykkes.
        await page.locator('#file').set_input_files(ROOT / 'examples/questionnaire-general.json')
        await page.evaluate('window.finishFileRead()')
        await expect(page.locator('#populate')).to_be_enabled()
        await page.locator('#populate').click()
        await result_matches_editor(page)


async def stale_response(page, status):
    ready, release = asyncio.Event(), asyncio.Event()

    async def delayed_response(route):
        response = await route.fetch()
        ready.set()
        await release.wait()
        if status == 200:
            await route.fulfill(response=response)
        else:
            await route.fulfill(status=status, json={'resourceType': 'OperationOutcome', 'issue': [
                {'details': {'text': 'Stale upstream error'}}]})

    await page.goto(BASE)
    await expect(page.locator('#populate')).to_be_enabled()
    await page.route('**/api/populate', delayed_response)
    await page.locator('#populate').click()
    await asyncio.wait_for(ready.wait(), timeout=10)
    # Kontroller revisjonsvernet også når input endres programmatisk mens editoren er låst.
    await page.locator('#questionnaire').evaluate("""(editor, text) => {
        editor.value = text;
        editor.dispatchEvent(new Event('input', {bubbles: true}));
    }""", GENERAL)
    release.set()
    await expect(page.locator('#populate')).to_be_enabled()
    await expect(page.locator('#output-result')).to_be_hidden()
    await expect(page.locator('#message')).to_be_hidden()
    assert json.loads(await page.locator('#questionnaire').input_value()) == json.loads(GENERAL)
    await page.unroute('**/api/populate', delayed_response)
    await page.locator('#populate').click()
    await result_matches_editor(page)


async def main():
    cases = [('Early input survives startup and enables controls', early_input),
             ('Failed configuration keeps remote actions disabled', config_failure)]
    for outcome in ('success', 'invalid-json', 'read-error'):
        cases.append(('File read locks controls and recovers: ' + outcome,
                      lambda page, outcome=outcome: pending_file(page, outcome)))
    for status in (200, 503):
        cases.append(('Stale response cannot publish results or errors: ' + str(status),
                      lambda page, status=status: stale_response(page, status)))
    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True, channel=os.environ.get('PLAYWRIGHT_CHANNEL') or None)
        try:
            for name, test in cases:
                context = await browser.new_context()
                try:
                    page = await context.new_page()
                    errors = []
                    page.on('pageerror', lambda error: errors.append(str(error)))
                    await test(page)
                    assert not errors, errors
                    print('PASS:', name)
                finally:
                    await context.close()
        finally:
            await browser.close()
    print(f'{len(cases)}/{len(cases)} async UI regressions passed.')


if __name__ == '__main__':
    asyncio.run(main())
