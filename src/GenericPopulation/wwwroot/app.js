'use strict';
// Grensesnitt for den lokale testappen. All preutfylling og tilgangskontroll skjer på serveren;
// nettleseren redigerer Q, sender pasientvalg og viser QR (QuestionnaireResponse) med merknader.
// Resultater holdes i minnet; lagring skjer bare gjennom brukerens nedlasting eller kopiering.
const $ = id => document.getElementById(id);
// config kommer fra /api/config; envelope er Parameters med QR + OperationOutcome; qr er selve svaret.
let config, envelope, qr, busy = false, initializing = true, inputRevision = 0;
const flatten = items => (items || []).flatMap(item => [item, ...flatten(item.item)]);
const pretty = value => JSON.stringify(value, null, 2);
const selectedSource = () => config?.sources.find(s => s.id === $('source').value);
// GET-kilder bruker en logisk Patient-ID. DHG bruker en syntetisk identifikator fra serverens testliste.
const isDhg = () => selectedSource()?.patientInput === 'identifier';
const patientKey = () => isDhg() ? $('test-patient').value : $('patient').value.trim();
function message(text, error = false) {
  $('message').textContent = text;
  $('message').classList.toggle('error', error);
  $('message').hidden = !text;
}
/** Fjern forrige resultat når kilde, pasient eller skjema endres, så gamle svar ikke vises som aktuelle. */
function invalidate() {
  inputRevision++;
  qr = envelope = null;
  $('output-result').hidden = true;
  $('output-empty').hidden = busy;
  message('');
}
/** Lås input under asynkrone kall og velg mellom tom, ventende og ferdig resultatvisning. */
function setBusy(value, activity = 'populate') {
  busy = value;
  for (const id of ['populate', 'source', 'patient', 'test-patient', 'example', 'upload', 'file'])
    $(id).disabled = value || initializing || !config;
  // Eget skjema kan redigeres mens konfigurasjonen lastes, også hvis oppstarten feiler.
  for (const id of ['format', 'questionnaire']) $(id).disabled = value;
  $('run-label').textContent = value ? (activity === 'questionnaire' ? 'Leser skjema …' : 'Preutfyller …') : 'Hent data og preutfyll';
  $('output-loading').querySelector('h3').textContent = activity === 'questionnaire' ? 'Leser skjema …' : 'Henter data fra FHIR …';
  $('output-loading').querySelector('p').textContent = activity === 'questionnaire'
    ? 'Venter på at Questionnaire er ferdig lastet.' : 'Leser Patient, kjører søk fra Q og bygger QR.';
  $('output-loading').hidden = !value;
  $('output-empty').hidden = value || !!qr;
  $('output-result').hidden = value || !qr;
}
/** Tilpass pasientvelger og eksempel til kilden. Et eget redigert skjema beholdes ved kildebytte. */
async function updateSource(loadDefaultExample = true) {
  invalidate();
  const source = selectedSource();
  const dhg = isDhg();
  $('endpoint').textContent = source?.baseUrl || '';
  $('patient').hidden = dhg;
  $('test-patient').hidden = !dhg;
  $('patient-label').textContent = dhg ? 'Syntetisk testperson (NIN)' : 'Patient-ID';
  $('patient-label').htmlFor = dhg ? 'test-patient' : 'patient';
  $('patient-help').textContent = dhg
    ? 'Bare godkjente syntetiske testpersoner. Oppslaget sendes til DHG Test når du trykker Hent data og preutfyll.'
    : 'Logisk ressurs-ID, for eksempel demo-patient.';
  $('test-patient').replaceChildren();
  for (const [index, identifier] of (source?.testPatientIdentifiers || []).entries()) {
    const option = element('option', '', `Testperson ${index + 1} · ${identifier}`);
    option.value = identifier;
    $('test-patient').append(option);
  }
  if (loadDefaultExample && $('example').value !== 'custom') {
    $('example').value = source?.defaultExample || 'pregnancy';
    await loadExample($('example').value);
  }
}
// Spørsmålsantall og versjon er en forhåndsvisning; serverens QuestionnaireGuard avgjør hva som støttes.
function updateInfo() {
  try {
    const q = JSON.parse($('questionnaire').value);
    const questions = flatten(q.item).filter(i => !['group', 'display'].includes(i.type));
    $('q-info').textContent = `${questions.length} spørsmål · versjon ${q.version || 'mangler'}`;
  } catch { $('q-info').textContent = 'JSON må være gyldig før preutfylling'; }
}
// Leser et lokalt Q-eksempel fra appen. Dette starter ingen oppslag hos en ekstern FHIR-kilde.
async function loadExample(name) {
  invalidate(); setBusy(true, 'questionnaire');
  const revision = inputRevision;
  try {
    const response = await fetch(`/api/examples/${encodeURIComponent(name)}`);
    if (!response.ok) throw new Error('Kunne ikke laste eksempelskjema.');
    const data = await response.json();
    if (revision !== inputRevision) return;
    $('questionnaire').value = pretty(data);
    updateInfo();
  } catch (error) { if (revision === inputRevision) message(error.message, true); }
  finally { setBusy(false); }
}
/** Hold synlig panel, ARIA-valg og tastaturfokus samordnet for resultatfanene. */
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
// Q- og FHIR-tekst settes med textContent, slik at data ikke blir tolket som HTML.
function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
/** Formater FHIR value[x] for visning uten å endre QR; false og 0 er gyldige svar. */
function answerText(answer) {
  const key = Object.keys(answer).find(k => k.startsWith('value'));
  const value = answer[key];
  if (key === 'valueQuantity') return `${value.value} ${value.unit || value.code || ''}`.trim();
  if (key === 'valueBoolean') return value ? 'Ja (true)' : 'Nei (false)';
  if (value !== null && typeof value === 'object') return pretty(value);
  return value == null ? '—' : String(value);
}
/** Følg QR-hierarkiet og bruk Qs linkId-kart for å skille grupper fra spørsmål. */
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
/** Vis svaret som lesbare felt, original QR JSON og separate merknader fra OperationOutcome. */
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
/** Valider enkel input, kall appens API og vis resultatet. Backend utfører alle FHIR-oppslag. */
async function populate() {
  if (busy || initializing || !config) return;
  invalidate();
  const revision = inputRevision;
  let questionnaire;
  try {
    questionnaire = JSON.parse($('questionnaire').value);
    if (questionnaire.resourceType !== 'Questionnaire') throw new Error('Input må være et FHIR Questionnaire.');
    if (isDhg()) {
      if (!/^[0-9]{11}$/.test(patientKey()) || !selectedSource().testPatientIdentifiers.includes(patientKey()))
        throw new Error('Velg en godkjent syntetisk testperson for DHG.');
    } else if (!/^[A-Za-z0-9.-]{1,64}$/.test(patientKey())) throw new Error('Skriv inn en gyldig logisk Patient-ID.');
  } catch (error) {
    message(error instanceof SyntaxError ? 'Ugyldig JSON. Kontroller komma, anførselstegn og parenteser i Questionnaire.' : error.message, true);
    return;
  }
  setBusy(true);
  const start = performance.now();
  try {
    // Send identifikatoren i JSON-kroppen, aldri i URL-en. Kildeadresse og token velges av serveren.
    // Nettleserfristen er litt lengre enn serverens 60 sekunder, så serverens feilsvar kan rekke frem.
    const response = await fetch('/api/populate', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ questionnaire, sourceId: $('source').value, [isDhg() ? 'patientIdentifier' : 'patientId']: patientKey() }),
      signal: AbortSignal.timeout(65000)
    });
    const data = await response.json();
    // Et sent svar eller en sen feil fra tidligere input skal aldri bli et aktuelt resultat.
    if (revision !== inputRevision) return;
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
    if (revision !== inputRevision) return;
    qr = envelope = null;
    message(error.name === 'TimeoutError' ? 'Tidsgrensen ble overskredet. Kontroller FHIR-kilden og prøv igjen.' : error.message, true);
  } finally { setBusy(false); }
}
/** Lag en lokal JSON-nedlasting og frigjør den midlertidige objekt-URL-en etterpå. */
function download(value, name) {
  if (!value) return;
  const url = URL.createObjectURL(new Blob([pretty(value)], { type: 'application/fhir+json' }));
  const link = element('a'); link.href = url; link.download = name;
  document.body.append(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
// Brukerhandlinger: inputendringer ugyldiggjør resultatet; bare preutfyllingshandlingen henter FHIR-data.
$('populate').addEventListener('click', populate);
$('source').addEventListener('change', () => updateSource());
$('patient').addEventListener('input', invalidate);
$('test-patient').addEventListener('change', invalidate);
$('questionnaire').addEventListener('input', () => { invalidate(); $('example').value = 'custom'; updateInfo(); });
$('example').addEventListener('change', () => loadExample($('example').value));
$('format').addEventListener('click', () => {
  try { $('questionnaire').value = pretty(JSON.parse($('questionnaire').value)); updateInfo(); message(''); }
  catch { message('JSON kunne ikke formateres. Kontroller syntaksen.', true); }
});
$('upload').addEventListener('click', () => $('file').click());
$('file').addEventListener('change', async () => {
  const file = $('file').files[0]; if (!file) return;
  if (busy || initializing || !config) { $('file').value = ''; return; }
  // Lås før første await; ellers kan preutfylling starte med det forrige skjemaet.
  invalidate(); setBusy(true, 'questionnaire');
  const revision = inputRevision;
  try {
    if (file.size > 240 * 1024) throw new Error('Q-filen kan ikke være større enn 240 KiB.');
    const data = JSON.parse(await file.text());
    if (revision !== inputRevision) return;
    if (data.resourceType !== 'Questionnaire') throw new Error('Filen må inneholde et FHIR Questionnaire.');
    $('questionnaire').value = pretty(data); $('example').value = 'custom'; updateInfo();
  } catch (error) { if (revision === inputRevision) message(error.message, true); }
  finally { $('file').value = ''; setBusy(false); }
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
// Oppstart henter kildevalg og et eksempelskjema fra egen server. Ingen DHG-oppslag kjøres her.
(async () => {
  setBusy(false);
  try {
    const response = await fetch('/api/config');
    if (!response.ok) throw new Error('Kunne ikke hente serverkonfigurasjon.');
    config = await response.json();
    for (const source of config.sources) {
      const option = element('option', '', source.name); option.value = source.id; $('source').append(option);
    }
    $('patient').value = config.defaultPatientId;
    await updateSource();
  } catch (error) { config = undefined; message('Kunne ikke starte appen: ' + error.message, true); }
  finally {
    initializing = false;
    // Aktiver også handlingene når brukeren allerede har limt inn eget Q.
    setBusy(false);
  }
})();
