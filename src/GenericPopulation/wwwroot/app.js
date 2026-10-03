'use strict';
const $ = id => document.getElementById(id);
let config, envelope, qr, busy = false;
const flatten = items => (items || []).flatMap(item => [item, ...flatten(item.item)]);
const pretty = value => JSON.stringify(value, null, 2);
function message(text, error = false) {
  $('message').textContent = text;
  $('message').classList.toggle('error', error);
  $('message').hidden = !text;
}
function invalidate() {
  qr = envelope = null;
  $('output-result').hidden = true;
  $('output-empty').hidden = false;
  message('');
}
function setBusy(value) {
  busy = value;
  for (const id of ['populate', 'source', 'patient', 'example', 'upload', 'format', 'questionnaire']) $(id).disabled = value;
  $('run-label').textContent = value ? 'Preutfyller …' : 'Hent data og preutfyll';
  $('output-loading').hidden = !value;
  $('output-empty').hidden = value || !!qr;
  $('output-result').hidden = value || !qr;
}
function updateInfo() {
  try {
    const q = JSON.parse($('questionnaire').value);
    const questions = flatten(q.item).filter(i => !['group', 'display'].includes(i.type));
    $('q-info').textContent = `${questions.length} spørsmål · versjon ${q.version || 'mangler'}`;
  } catch { $('q-info').textContent = 'JSON må være gyldig før preutfylling'; }
}
async function loadExample(name) {
  invalidate(); setBusy(true);
  try {
    const response = await fetch(`/api/examples/${encodeURIComponent(name)}`);
    if (!response.ok) throw new Error('Kunne ikke laste eksempelskjema.');
    $('questionnaire').value = pretty(await response.json());
    updateInfo();
  } catch (error) { message(error.message, true); }
  finally { setBusy(false); }
}
function selectTab(panel, focus = false) {
  for (const tab of document.querySelectorAll('.tab')) {
    const selected = tab.dataset.panel === panel;
    tab.classList.toggle('active', selected);
    tab.setAttribute('aria-selected', selected);
    tab.tabIndex = selected ? 0 : -1;
    $(tab.dataset.panel).hidden = !selected;
    if (selected && focus) tab.focus();
  }
}
function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
function answerText(answer) {
  const key = Object.keys(answer).find(k => k.startsWith('value'));
  const value = answer[key];
  if (key === 'valueQuantity') return `${value.value} ${value.unit || value.code || ''}`.trim();
  if (key === 'valueBoolean') return value ? 'Ja (true)' : 'Nei (false)';
  if (value !== null && typeof value === 'object') return pretty(value);
  return value == null ? '—' : String(value);
}
function renderItems(items, parent, questionMap) {
  for (const item of items || []) {
    const question = questionMap.get(item.linkId);
    if (question?.type === 'group' || item.item) {
      parent.append(element('h3', 'group-title', item.text || item.linkId));
      renderItems(item.item, parent, questionMap);
    } else {
      const row = element('div', 'answer-row');
      const label = element('div', 'answer-label', item.text || item.linkId);
      label.append(element('span', 'answer-id', item.linkId));
      const hasAnswer = !!item.answer?.length;
      row.append(label, element('div', 'answer-value' + (hasAnswer ? '' : ' missing'), hasAnswer ? item.answer.map(answerText).join('\n') : 'Ikke utfylt'));
      parent.append(row);
    }
  }
}
function showResult(questionnaire, elapsed, requestCount) {
  const issues = envelope.parameter.find(p => p.name === 'issues')?.resource?.issue || [];
  const questions = flatten(questionnaire.item).filter(i => !['group', 'display'].includes(i.type));
  const answers = flatten(qr.item).filter(i => i.answer?.length);
  $('answered').textContent = `${answers.length}/${questions.length}`;
  $('requests').textContent = requestCount || '—';
  $('duration').textContent = elapsed < 1000 ? `${Math.round(elapsed)} ms` : `${(elapsed / 1000).toFixed(1)} s`;
  $('result-title').textContent = issues.some(i => i.severity === 'warning') ? 'Preutfylt med merknader' : 'Preutfylling gjennomført';
  $('qr-json').textContent = pretty(qr);
  $('preview').replaceChildren();
  renderItems(qr.item, $('preview'), new Map(flatten(questionnaire.item).map(q => [q.linkId, q])));
  $('issue-count').textContent = issues.length;
  $('issues').replaceChildren();
  for (const issue of issues) {
    const node = element('div', 'issue' + (issue.severity === 'warning' ? ' warning' : ''));
    node.append(element('strong', '', issue.severity === 'warning' ? 'Kontroller feltet' : 'Informasjon'));
    node.append(element('p', '', issue.details?.text || 'Merknad fra preutfyllingen.'));
    if (issue.diagnostics) node.append(element('small', '', issue.diagnostics));
    $('issues').append(node);
  }
  selectTab('preview');
}
async function populate() {
  if (busy || !config) return;
  invalidate();
  let questionnaire;
  try {
    questionnaire = JSON.parse($('questionnaire').value);
    if (questionnaire.resourceType !== 'Questionnaire') throw new Error('Input må være et FHIR Questionnaire.');
    if (!/^[A-Za-z0-9.-]{1,64}$/.test($('patient').value.trim())) throw new Error('Skriv inn en gyldig logisk Patient-ID.');
  } catch (error) {
    message(error instanceof SyntaxError ? 'Ugyldig JSON. Kontroller komma, anførselstegn og parenteser i Questionnaire.' : error.message, true);
    return;
  }
  setBusy(true);
  const start = performance.now();
  try {
    const response = await fetch('/api/populate', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ questionnaire, sourceId: $('source').value, patientId: $('patient').value.trim() }),
      signal: AbortSignal.timeout(65000)
    });
    const data = await response.json();
    if (!response.ok) {
      const details = (data.issue || []).map(i => i.details?.text || i.diagnostics).filter(Boolean).join('\n');
      throw new Error(details || `Preutfylling feilet (HTTP ${response.status}).`);
    }
    envelope = data;
    qr = data.parameter?.find(p => p.name === 'response')?.resource;
    if (!qr || qr.resourceType !== 'QuestionnaireResponse') throw new Error('Serveren returnerte ikke en QuestionnaireResponse.');
    showResult(questionnaire, performance.now() - start, response.headers.get('X-Fhir-Requests'));
    message('QR er klar. Kontroller forhåndsvisningen og merknadene før videre bruk.');
  } catch (error) {
    qr = envelope = null;
    message(error.name === 'TimeoutError' ? 'Tidsgrensen ble overskredet. Kontroller FHIR-kilden og prøv igjen.' : error.message, true);
  } finally { setBusy(false); }
}
function download(value, name) {
  if (!value) return;
  const url = URL.createObjectURL(new Blob([pretty(value)], { type: 'application/fhir+json' }));
  const link = element('a'); link.href = url; link.download = name;
  document.body.append(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
$('populate').addEventListener('click', populate);
$('source').addEventListener('change', () => {
  invalidate(); $('endpoint').textContent = config.sources.find(s => s.id === $('source').value)?.baseUrl || '';
});
$('patient').addEventListener('input', invalidate);
$('questionnaire').addEventListener('input', () => { invalidate(); $('example').value = 'custom'; updateInfo(); });
$('example').addEventListener('change', () => loadExample($('example').value));
$('format').addEventListener('click', () => {
  try { $('questionnaire').value = pretty(JSON.parse($('questionnaire').value)); updateInfo(); message(''); }
  catch { message('JSON kunne ikke formateres. Kontroller syntaksen.', true); }
});
$('upload').addEventListener('click', () => $('file').click());
$('file').addEventListener('change', async () => {
  const file = $('file').files[0]; if (!file) return;
  try {
    if (file.size > 240 * 1024) throw new Error('Q-filen kan ikke være større enn 240 KiB.');
    const data = JSON.parse(await file.text());
    if (data.resourceType !== 'Questionnaire') throw new Error('Filen må inneholde et FHIR Questionnaire.');
    invalidate(); $('questionnaire').value = pretty(data); $('example').value = 'custom'; updateInfo();
  } catch (error) { message(error.message, true); }
  finally { $('file').value = ''; }
});
for (const tab of document.querySelectorAll('.tab')) {
  tab.addEventListener('click', () => selectTab(tab.dataset.panel));
  tab.addEventListener('keydown', event => {
    const tabs = [...document.querySelectorAll('.tab')];
    const index = tabs.indexOf(tab);
    const next = event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index + tabs.length - 1) % tabs.length : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : null;
    if (next !== null) { event.preventDefault(); selectTab(tabs[next].dataset.panel, true); }
  });
}
$('download').addEventListener('click', () => download(qr, 'questionnaire-response.json'));
$('download-envelope').addEventListener('click', () => download(envelope, 'population-result.json'));
$('copy').addEventListener('click', async () => {
  try { if (qr) { await navigator.clipboard.writeText(pretty(qr)); message('QR JSON er kopiert.'); } }
  catch { message('Nettleseren tillot ikke kopiering. Bruk Last ned QR.', true); }
});
document.addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') { event.preventDefault(); populate(); }
});
(async () => {
  try {
    const response = await fetch('/api/config');
    if (!response.ok) throw new Error('Kunne ikke hente serverkonfigurasjon.');
    config = await response.json();
    for (const source of config.sources) {
      const option = element('option', '', source.name); option.value = source.id; $('source').append(option);
    }
    $('endpoint').textContent = config.sources[0].baseUrl;
    $('patient').value = config.defaultPatientId;
    await loadExample('pregnancy');
  } catch (error) { message('Kunne ikke starte appen: ' + error.message, true); }
})();
