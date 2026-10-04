'use strict';
// Lokalt utkast i minnet. Bare «Lagre og ta i bruk» skriver til serveren. Ingen tokenverdier hentes.
const $ = id => document.getElementById(id);
const pretty = value => JSON.stringify(value, null, 2);
let draft, revision, kind = 'Sources', index = 0, dirty = false, busy = false;
const titles = { Sources: 'Kilder', PopulationProfiles: 'Populeringsprofiler', QuestionnaireBindings: 'Skjemaversjoner' };
const help = {
  Sources: 'Endepunkter, transport og API-begrensninger. En kilde kan brukes av flere profiler.',
  PopulationProfiles: 'Sentral pasientkilde og ruteregler. Enkeltkildeprofiler følger automatisk kildeinnstillingene.',
  QuestionnaireBindings: 'Koble eksakt Questionnaire-URL og versjon til profilen som skal brukes.'
};
const field = (key, label, type = 'text', hint = '', values = null) => ({ key, label, type, hint, values });
const examples = ['pregnancy', 'general', 'dhg'];
const fields = {
  Sources: [field('Id', 'Kilde-ID', 'text', 'Unik ID med bokstaver, tall, bindestrek eller understrek.'), field('Name', 'Visningsnavn'),
    field('BaseUrl', 'FHIR-base URL', 'url', 'HTTPS og avsluttende skråstrek. HTTP er tillatt på loopback.'), field('SearchMethod', 'Søkemetode', 'select', '', ['GET', 'POST']),
    field('ExposeAsProfile', 'Vis også som enkeltkildeprofil', 'checkbox'), field('DefaultExample', 'Standardeksempel', 'select', '', examples),
    field('BearerTokenEnvironmentVariable', 'Miljøvariabel for bearer-token', 'optional', 'Bare variabelnavnet, eksempelvis FHIR_TEST_TOKEN. Aldri selve tokenet.'),
    field('PatientIdentifierPattern', 'Valgfritt identifikatormønster', 'optional', 'Regulært uttrykk for sentralt Patient-søk.'),
    field('AllowedTestPatientIdentifiers', 'Tillatte syntetiske testidentifikatorer', 'lines', 'Én per linje. Tom liste gir fri identifikatorinput; listen vises i appen.'),
    field('PatientLookup', 'Sentralt pasientoppslag · JSON', 'json', 'Interaction: read eller search. Ved search brukes Parameter, vanligvis identifier.'),
    field('PatientBinding', 'Pasientfilter · JSON', 'json', 'Parameter er filteret hos endepunktet. ValueFrom: patientId, inputIdentifier eller patientIdentifier.'),
    field('Capabilities', 'API-begrensninger · JSON', 'json', 'Paging, Resources, SearchParameters og øvrige begrensninger for denne kilden.')],
  PopulationProfiles: [field('Id', 'Profil-ID'), field('Name', 'Visningsnavn'),
    field('PatientSource', 'Sentral pasientkilde', 'source'), field('DefaultSource', 'Standardkilde', 'optional-source', 'Brukes når ingen mer presis regel treffer.'),
    field('DefaultExample', 'Standardeksempel', 'select', '', examples), field('OptionalSources', 'Valgfrie kilder', 'lines', 'Én kilde-ID per linje. Sentral pasientkilde er alltid påkrevd.'),
    field('Routes', 'Ruteregler · JSON', 'json', 'Eksempel: [{"ResourceType":"Observation","Source":"demo"}]. Code og Profile kan avgrense regelen.'),
    field('QueryBindings', 'Koblinger per søkevariabel · JSON', 'json', 'Questionnaire, Version, Variable, Source og eventuelt LinkId velger en bestemt variable-extension.')],
  QuestionnaireBindings: [field('Questionnaire', 'Questionnaire-URL', 'url', 'Skjemaets canonical URL, uten |versjon.'),
    field('Version', 'Eksakt versjon', 'text', 'Må stemme nøyaktig med Questionnaire.version.'),
    field('ProfileId', 'Populeringsprofil', 'profile', 'Denne profilen velges automatisk for skjemaversjonen.')]
};

function node(tag, className, text) {
  const el = document.createElement(tag);
  if (className) el.className = className;
  if (text !== undefined) el.textContent = text;
  return el;
}
function message(text, error = false) {
  $('config-message').textContent = text;
  $('config-message').classList.toggle('error', error);
  $('config-message').hidden = !text;
}
function setBusy(value) {
  busy = value;
  for (const control of document.querySelectorAll('main button, main input, main select, main textarea')) control.disabled = value || !draft;
  $('config-fields').disabled = value || !draft;
  $('config-reload').disabled = value;
  for (const id of ['config-remove', 'config-duplicate']) $(id).disabled = value || !draft?.[kind]?.[index];
  $('config-state').textContent = value ? 'Arbeider …' : dirty ? 'Ulagrede endringer' : draft ? 'Lagret' : 'Ikke lastet';
}
function markDirty() { dirty = true; $('config-state').textContent = 'Ulagrede endringer'; message(''); }
function profileChoices() {
  return [...draft.Sources.filter(s => s.ExposeAsProfile !== false).map(s => [s.Id, `${s.Name} · enkeltkilde`]),
    ...draft.PopulationProfiles.map(p => [p.Id, p.Name])];
}
function choices(definition) {
  if (definition.type === 'profile') return profileChoices();
  if (definition.type.includes('source')) return draft.Sources.map(s => [s.Id, s.Name]);
  return definition.values.map(v => [v, v]);
}
function itemLabel(item, itemKind = kind) {
  return itemKind === 'QuestionnaireBindings' ? [item.Questionnaire || 'Ny skjemakobling', `Versjon ${item.Version || '…'} → ${item.ProfileId || '…'}`]
    : [item.Name || item.Id || 'Ny oppføring', itemKind === 'Sources' ? `${item.Id || '…'} · ${item.SearchMethod || 'GET'} · ${item.BaseUrl || 'URL mangler'}` : `${item.Id || '…'} · Patient: ${item.PatientSource || '…'}`];
}
function renderList() {
  $('config-list-title').textContent = titles[kind]; $('config-list-help').textContent = help[kind];
  for (const button of document.querySelectorAll('[data-kind]')) button.setAttribute('aria-pressed', String(button.dataset.kind === kind));
  const list = $('config-list'); list.replaceChildren();
  draft[kind].forEach((item, position) => {
    const [title, detail] = itemLabel(item);
    const button = node('button', 'config-entry'); button.type = 'button'; button.dataset.index = position;
    button.setAttribute('aria-current', String(index === position));
    button.append(node('strong', '', title), node('span', '', detail));
    button.addEventListener('click', () => select(kind, position)); list.append(button);
  });
  if (kind === 'PopulationProfiles') draft.Sources.forEach((source, position) => {
    if (source.ExposeAsProfile === false) return;
    const button = node('button', 'config-entry'); button.type = 'button';
    button.append(node('strong', '', source.Name), node('span', '', `${source.Id} · automatisk fra kilde. Åpne kilden for å redigere.`));
    button.addEventListener('click', () => select('Sources', position)); list.append(button);
  });
  if (!list.childElementCount) list.append(node('p', 'config-empty', 'Ingen oppføringer. Bruk «Legg til» for å opprette en.'));
}
function renderEditor() {
  const item = draft[kind][index], container = $('config-fields'); container.replaceChildren();
  $('config-empty').hidden = !!item;
  $('config-empty').textContent = 'Velg en oppføring eller legg til en ny.';
  $('config-editor-title').textContent = item ? itemLabel(item)[0] : 'Velg en oppføring';
  if (!item) return;
  for (const definition of fields[kind]) {
    const { key, label, type, hint } = definition;
    const group = node('div', 'field config-field' + (['json', 'lines', 'url'].includes(type) ? ' full-width' : ''));
    const labelElement = node('label', '', label); labelElement.htmlFor = 'cfg-' + key;
    let input;
    if (type === 'json' || type === 'lines') {
      input = node('textarea', type === 'lines' ? 'line-list' : '');
      input.value = type === 'json' ? pretty(item[key] ?? (['Routes', 'QueryBindings'].includes(key) ? [] : {})) : (item[key] || []).join('\n');
    } else if (['select', 'source', 'optional-source', 'profile'].includes(type)) {
      input = node('select'); const values = choices(definition);
      if (!values.some(([value]) => value === item[key])) values.unshift([item[key] || '', item[key] ? `${item[key]} · finnes ikke` : 'Velg …']);
      if (type === 'optional-source' && !values.some(([value]) => !value)) values.unshift(['', 'Ingen standardkilde']);
      for (const [value, text] of values) { const option = node('option', '', text); option.value = value; input.append(option); }
      input.value = item[key] || '';
    } else {
      input = node('input'); input.type = type === 'checkbox' ? 'checkbox' : type === 'url' ? 'url' : 'text';
      if (type === 'checkbox') input.checked = item[key] !== false; else input.value = item[key] || '';
    }
    input.id = 'cfg-' + key; input.autocomplete = 'off'; input.spellcheck = false;
    if (hint) input.setAttribute('aria-describedby', 'hint-' + key);
    group.append(labelElement, input);
    if (hint) { const note = node('span', 'field-help', hint); note.id = 'hint-' + key; group.append(note); }
    container.append(group);
  }
}
// Les alle felter før bytte, eksport eller lagring. Ugyldig JSON blir stående synlig i editoren.
function applyEditor() {
  if (!draft?.[kind]?.[index]) return;
  const updated = { ...draft[kind][index] };
  for (const { key, type, label } of fields[kind]) {
    const input = $('cfg-' + key);
    if (type === 'json') {
      try { updated[key] = JSON.parse(input.value); }
      catch { throw new Error(`${label} inneholder ugyldig JSON. Rett feltet før du fortsetter.`); }
    } else if (type === 'checkbox') updated[key] = input.checked;
    else if (type === 'lines') updated[key] = input.value.split(/\r?\n/).map(v => v.trim()).filter(Boolean);
    else updated[key] = input.value || (type.startsWith('optional') ? null : '');
  }
  draft[kind][index] = updated;
}
function select(nextKind, position) {
  if (busy) return;
  try { applyEditor(); kind = nextKind; index = position; renderList(); renderEditor(); setBusy(false); }
  catch (error) { message(error.message, true); }
}
function uniqueId(base, items) { let candidate = base, n = 1; while (items.some(i => i.Id === candidate)) candidate = base + '-' + n++; return candidate; }
function add(duplicate = false) {
  try {
    applyEditor(); let item;
    if (duplicate) {
      item = structuredClone(draft[kind][index]);
      if (kind === 'QuestionnaireBindings') item.Version = '';
      else { item.Id = uniqueId(item.Id + '-copy', [...draft.Sources, ...draft.PopulationProfiles]); item.Name += ' · kopi'; }
    } else if (kind === 'Sources') item = { Id: uniqueId('new-source', [...draft.Sources, ...draft.PopulationProfiles]), Name: 'Ny FHIR-kilde', BaseUrl: '', SearchMethod: 'GET', ExposeAsProfile: true,
      DefaultExample: 'pregnancy', PatientLookup: { Interaction: 'read', Parameter: 'identifier' }, PatientBinding: { Parameter: 'patient', ValueFrom: 'patientId' }, Capabilities: { Paging: 'follow', Resources: {} }, AllowedTestPatientIdentifiers: [] };
    else if (kind === 'PopulationProfiles') item = { Id: uniqueId('new-profile', [...draft.Sources, ...draft.PopulationProfiles]), Name: 'Ny populeringsprofil', PatientSource: draft.Sources[0]?.Id || '', DefaultSource: draft.Sources[0]?.Id || '', DefaultExample: 'pregnancy', Routes: [], QueryBindings: [], OptionalSources: [] };
    else item = { Questionnaire: '', Version: '1.0.0', ProfileId: profileChoices()[0]?.[0] || '' };
    draft[kind].push(item); index = draft[kind].length - 1;
    markDirty(); renderList(); renderEditor(); setBusy(false); $('config-fields').querySelector('input,select')?.focus();
  } catch (error) { message(error.message, true); }
}
async function api(path, body) {
  const response = await fetch(path, body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {});
  const result = await response.json();
  if (!response.ok) throw new Error(result.issue?.map(i => i.details?.text).filter(Boolean).join('\n') || `Forespørselen feilet (HTTP ${response.status}).`);
  return result;
}
function accept(result) {
  draft = result.configuration; revision = result.revision; dirty = false;
  $('config-storage').textContent = result.storage;
  $('config-require-binding').checked = draft.RequireQuestionnaireBinding;
  index = Math.min(index, draft[kind].length - 1); renderList(); renderEditor();
}
async function reload() {
  if (dirty && !window.confirm('Forkaste utkastet og hente lagret konfigurasjon?')) return;
  setBusy(true);
  try { accept(await api('/api/configuration')); message('Lagret konfigurasjon er lastet.'); }
  catch (error) { message(error.message, true); }
  finally { setBusy(false); }
}
async function submit(save) {
  try { applyEditor(); } catch (error) { message(error.message, true); return; }
  setBusy(true);
  try {
    const result = await api(save ? '/api/configuration' : '/api/configuration/validate', { revision, configuration: draft });
    if (save) accept(result);
    message(save ? 'Konfigurasjonen er lagret og aktiv for nye preutfyllinger. Åpne preutfyllingssiden på nytt for å bruke oppsettet.' : 'Utkastet er gyldig. Ingen endringer er lagret.');
  } catch (error) { message(error.message, true); }
  finally { setBusy(false); }
}
$('config-form').addEventListener('submit', event => event.preventDefault());
$('config-form').addEventListener('input', () => { markDirty(); try { applyEditor(); renderList(); } catch { /* Bevar uferdig JSON mens brukeren skriver. */ } });
for (const button of document.querySelectorAll('[data-kind]')) button.addEventListener('click', () => select(button.dataset.kind, 0));
$('config-add').addEventListener('click', () => add());
$('config-duplicate').addEventListener('click', () => add(true));
$('config-remove').addEventListener('click', () => { draft[kind].splice(index, 1); index = Math.min(index, draft[kind].length - 1); markDirty(); renderList(); renderEditor(); setBusy(false); });
$('config-require-binding').addEventListener('change', event => { draft.RequireQuestionnaireBinding = event.target.checked; markDirty(); });
$('config-reload').addEventListener('click', reload);
$('config-save').addEventListener('click', () => submit(true));
$('config-validate').addEventListener('click', () => submit(false));
$('config-export').addEventListener('click', () => {
  try {
    applyEditor(); const url = URL.createObjectURL(new Blob([pretty(draft)], { type: 'application/json' }));
    const link = node('a'); link.href = url; link.download = 'fhir-configuration.json'; document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (error) { message(error.message, true); }
});
window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
reload();
