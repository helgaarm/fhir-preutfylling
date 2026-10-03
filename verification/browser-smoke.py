"""Optional UI tests: pip install playwright && python -m playwright install chromium."""
import json
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--no-sandbox'])
    page = browser.new_page(viewport={'width': 1440, 'height': 1100}, device_scale_factor=1)
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.goto('http://127.0.0.1:5077')
    expect(page.locator('#populate')).to_be_enabled()
    expect(page.locator('#q-info')).to_contain_text('6 spørsmål')
    page.locator('#populate').click()
    expect(page.locator('#output-result')).to_be_visible()
    expect(page.locator('#answered')).to_have_text('5/6')
    expect(page.locator('#requests')).to_have_text('3')
    expect(page.locator('#preview')).to_contain_text('210 dager')
    expect(page.locator('#preview')).to_contain_text('Ikke utfylt')
    page.screenshot(path=str(ROOT / 'verification/app-desktop.png'), full_page=True)
    with page.expect_download() as download:
        page.locator('#download').click()
    resource = json.loads(Path(download.value.path()).read_text())
    assert resource['resourceType'] == 'QuestionnaireResponse'
    page.locator('#tab-json').click()
    expect(page.locator('#json')).to_be_visible()
    page.locator('#tab-json').press('ArrowRight')
    expect(page.locator('#issues')).to_be_visible()
    page.locator('#example').select_option('general')
    expect(page.locator('#q-info')).to_contain_text('4 spørsmål')
    expect(page.locator('#output-result')).to_be_hidden()
    page.locator('#populate').click()
    expect(page.locator('#preview')).to_contain_text('Nei (false)')
    expect(page.locator('#preview')).to_contain_text('Ada\nDemo')
    page.locator('#questionnaire').fill('{invalid')
    page.locator('#populate').click()
    expect(page.locator('#message')).to_contain_text('Ugyldig JSON')
    expect(page.locator('#output-result')).to_be_hidden()
    page.locator('#file').set_input_files(ROOT / 'examples/questionnaire-pregnancy.json')
    expect(page.locator('#q-info')).to_contain_text('6 spørsmål')
    page.locator('#patient').fill('unknown')
    page.locator('#populate').click()
    expect(page.locator('#message')).to_contain_text('HTTP 404')
    expect(page.locator('#output-result')).to_be_hidden()
    page.locator('#patient').fill('demo-patient')
    page.locator('#questionnaire').press('Control+Enter')
    expect(page.locator('#output-result')).to_be_visible()
    page.set_viewport_size({'width': 390, 'height': 844})
    page.screenshot(path=str(ROOT / 'verification/app-mobile.png'), full_page=True)
    assert page.evaluate('document.documentElement.scrollWidth <= window.innerWidth'), 'Mobile overflow'
    assert not errors, errors
    browser.close()
print('PASS: UI examples, HTTP population, false, repeated answers, file upload, invalid JSON, missing patient, tabs, keyboard, QR download and mobile layout. No JavaScript errors.')
