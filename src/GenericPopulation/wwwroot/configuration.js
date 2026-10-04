'use strict';
// Lokalt utkast i minnet. Bare «Lagre og ta i bruk» skriver til serveren. Ingen tokenverdier hentes.
const $ = id => document.getElementById(id);
const pretty = value => JSON.stringify(value, null, 2);
let draft, revision, kind = 'Sources', index = 0, dirty = false, busy = false;
let endpointCheck;
const titles = { Sources: 'Kilder', PopulationProfiles: 'Populeringsprofiler', QuestionnaireBindings: 'Skjemaversjoner' };
const help = {
  Sources: 'Velg en FHIR-server for å endre adresse, pasientoppslag og søk. Samme kilde kan brukes av flere profiler.',
  PopulationProfiles: 'Velg hvor pasienten og de kliniske dataene hentes. Profiler merket «automatisk fra kilde» redigeres under Kilder.',
  QuestionnaireBindings: 'Koble en bestemt skjemaversjon til en profil. Dette velger og låser profilen på preutfyllingssiden; selve skjemaet endres ikke.'
};
const field = (key, label, type = 'text', hint = '', values = null) => ({ key, label, type, hint, values });
const examples = [['pregnancy', 'Svangerskapsopplysninger'], ['general', 'Personopplysninger'], ['dhg', 'DHG – svangerskap og kontakter']];
const fields = {
  Sources: [field('Id', 'Kilde-ID', 'text', 'Unik nøkkel, for eksempel min-fhir. Bruk bokstaver, tall, bindestrek eller understrek. Endrer du ID-en, må profiler som viser til den også oppdateres.'),
    field('Name', 'Visningsnavn', 'text', 'Navnet som vises i kilde- og profilvalgene, for eksempel «Lokal testserver».'),
    field('BaseUrl', 'FHIR-base URL', 'url', 'Rotadressen til FHIR-API-et, for eksempel https://fhir.example.no/r4/. Avslutt med /. Ikke legg til Patient, søkeparametere eller token. HTTP er bare tillatt lokalt, for eksempel på 127.0.0.1.'),
    field('SearchMethod', 'Søkemetode', 'select', 'Velg det serveren støtter: GET sender søkeparametere i URL-en. POST sender dem i en formkropp til ressurstypens /_search. Direkte Patient-oppslag bruker fortsatt GET.', ['GET', 'POST']),
    field('ExposeAsProfile', 'Vis også som enkeltkildeprofil', 'checkbox', 'På: kilden blir også en profil med samme ID i preutfyllingen, og alle data hentes herfra. Slå av hvis kilden bare skal inngå i en profil med flere kilder.'),
    field('DefaultExample', 'Standardeksempel', 'select', 'Eksempelskjemaet som foreslås når enkeltkildeprofilen velges. Et eget opplastet eller redigert skjema beholdes.', examples),
    field('BearerTokenEnvironmentVariable', 'Miljøvariabel for bearer-token', 'optional', 'La stå tomt uten bearer-autentisering. Ellers: skriv bare variabelnavnet, for eksempel FHIR_TEST_TOKEN. Sett selve tokenet i serverens miljø før appen startes; det skal aldri limes inn her.'),
    field('PatientIdentifierPattern', 'Valgfritt identifikatormønster', 'optional', 'Gjelder når sentralt pasientoppslag bruker search. Et regulært uttrykk kontrollerer input før oppslag, for eksempel ^[0-9]{11}$ for elleve sifre. Tomt felt gir ingen ekstra mønsterkontroll.'),
    field('AllowedTestPatientIdentifiers', 'Tillatte syntetiske testidentifikatorer', 'lines', 'Ved pasientoppslag med search: én godkjent syntetisk identifikator per linje. Listen blir en pasientvelger, og andre verdier avvises. Tom liste gir et fritt tekstfelt, fortsatt kontrollert av eventuelt identifikatormønster.'),
    field('PatientLookup', 'Sentralt pasientoppslag · JSON', 'json', 'Bestemmer hvordan Patient hentes når denne kilden er sentral pasientkilde: direkte med en ressurs-ID (read), eller ved søk etter en identifikator (search).'),
    field('PatientBinding', 'Pasientfilter · JSON', 'json', 'Bestemmer hvordan kliniske søk avgrenses til pasienten som allerede er hentet. Tilpass parameternavn og identifikator til det denne serveren forventer.'),
    field('Capabilities', 'API-begrensninger · JSON', 'json', 'Beskriver hvilke kliniske søk kilden tillater og hvordan søkeresultater håndteres. Dette er avtalte begrensninger du setter selv; appen oppdager dem ikke automatisk.')],
  PopulationProfiles: [field('Id', 'Profil-ID', 'text', 'Unik nøkkel, for eksempel min-profil. Bruk bokstaver, tall, bindestrek eller understrek. ID-en må også være ulik automatisk opprettede enkeltkildeprofiler.'),
    field('Name', 'Visningsnavn', 'text', 'Navnet brukeren ser i profilvelgeren på preutfyllingssiden.'),
    field('PatientSource', 'Sentral pasientkilde', 'source', 'Patient hentes én gang fra denne kilden før andre søk. Kilden bestemmer om brukeren oppgir Patient-ID eller en identifikator. De øvrige kildene må kunne bruke denne pasientidentiteten.'),
    field('DefaultSource', 'Standardkilde', 'optional-source', 'Kilden for kliniske søk som ingen ruteregel eller søkevariabelkobling treffer. Med «Ingen standardkilde» må hvert søk ha en regel, ellers stoppes preutfyllingen.'),
    field('DefaultExample', 'Standardeksempel', 'select', 'Eksempelskjemaet som foreslås når profilen velges. Dette kobler ikke en skjemaversjon til profilen; det gjør du under Skjemaer.', examples),
    field('OptionalSources', 'Valgfrie kilder', 'lines', 'Én kilde-ID per linje, blant kildene profilen bruker. Ved enkelte tilgjengelighetsfeil kan data fra disse utelates med en merknad. Tom liste betyr at alle er påkrevd. Sentral pasientkilde kan aldri være valgfri; pasientavvik og ugyldige data stopper alltid preutfyllingen.'),
    field('Routes', 'Ruteregler · JSON', 'json', 'Fordeler kliniske søk på kilder etter ressurstype og eventuelt kode eller FHIR-profil. Den mest presise regelen vinner, uansett rekkefølge. Bruk [] hvis standardkilden skal håndtere alle søk uten en søkevariabelkobling.'),
    field('QueryBindings', 'Koblinger per søkevariabel · JSON', 'json', 'Overstyr kilde for én navngitt søkevariabel i en bestemt skjemaversjon. Disse koblingene går foran rutereglene. La stå som [] hvis du ikke trenger så detaljert styring.')],
  QuestionnaireBindings: [field('Questionnaire', 'Questionnaire-URL', 'url', 'Kopier verdien fra url i Questionnaire JSON, for eksempel https://example.org/fhir/Questionnaire/pregnancy-demo. Dette er skjemaets identitet, ikke filadressen. Ikke ta med |versjon.'),
    field('Version', 'Eksakt versjon', 'text', 'Kopier version fra samme Questionnaire, for eksempel 1.0.0. Teksten må stemme nøyaktig: 1.0 og 1.0.0 er ulike versjoner. Registrer hver versjon som skal brukes.'),
    field('ProfileId', 'Populeringsprofil', 'profile', 'Velges og låses automatisk når skjemaets URL og versjon stemmer. Flere skjemaer kan bruke samme profil. Valgene omfatter også automatiske enkeltkildeprofiler.')]
};

// Utfyllende hjelp ligger ved JSON-feltet og kan åpnes uten å endre utkastet.
const jsonGuides = {
  PatientLookup: {
    terms: [
      ['Interaction', 'read henter Patient/{id}, for eksempel Patient/demo-patient. search søker etter en identifikator og krever én entydig pasient.'],
      ['Parameter', 'Søkeparameteren ved search, vanligvis identifier. Brukes ikke ved read. Søkemetoden GET eller POST velges i feltet over.'],
      ['RequireDistinctResourceId', 'true krever at returnert Patient-ID er ulik inputidentifikatoren. La være false med mindre serveravtalen krever dette.']
    ],
    caption: 'Eksempel på direkte oppslag med Patient-ID (lokal demo):',
    example: { Interaction: 'read', Parameter: 'identifier', RequireDistinctResourceId: false }
  },
  PatientBinding: {
    terms: [
      ['Parameter', 'Serverens pasientfilter i kliniske søk, vanligvis patient. Enkelte servere bruker patient.identifier eller subject.'],
      ['ValueFrom', 'patientId bruker Patient.id fra sentralt oppslag. inputIdentifier bruker identifikatoren som ble oppgitt ved sentralt search-oppslag. patientIdentifier leser en identifikator fra den hentede Patient-ressursen.'],
      ['IdentifierSystem', 'Kreves bare ved patientIdentifier. Angi identifikatorsystemet; pasienten må ha én entydig verdi for dette systemet. Filteret sendes som system|verdi.']
    ],
    caption: 'Eksempel: send patient=demo-patient når den hentede pasienten har ID demo-patient:',
    example: { Parameter: 'patient', ValueFrom: 'patientId' }
  },
  Capabilities: {
    terms: [
      ['Paging', 'follow følger neste side innenfor appens grenser. none avviser resultater med neste-lenke, og resultater der total viser at noe mangler.'],
      ['Resources', 'Et tomt objekt {} legger ikke til en egen liste over tillatte ressurstyper. Med oppføringer tillates bare disse typene i kliniske søk. Sentralt Patient-oppslag styres separat.'],
      ['SearchParameters', 'Tillatte parametere per ressurstype, etter at pasientfilteret er oversatt. Ta med det faktiske pasientfilteret. Utelat feltet for ingen ekstra parameterliste.'],
      ['PatientReferencePath', 'Elementstien til pasientreferansen som kontrolleres i returnerte ressurser. Standard er subject; enkelte typer bruker patient.'],
      ['RequireTotal / RejectOutcomeEntries', 'Begge er false som standard. true krever henholdsvis Bundle.total eller avviser OperationOutcome-oppføringer. Feil og fatale utfall avvises alltid.'],
      ['MaxFormBytes', 'Maksimal formkropp ved POST-søk, fra 1 til 65536 byte. Standard er 65536.'],
      ['MaxOccurrences / TokenParameters / TokenSystems / DateParameters', 'Valgfrie regler per ressurstype for antall parameterforekomster, system|kode-format, tillatte kodesystemer og datoformat.']
    ],
    caption: 'Eksempel for en kilde som bare tillater Observation-søk med patient og code. Tilpass listen til søkene i skjemaet:',
    example: { Paging: 'follow', Resources: { Observation: { SearchParameters: ['patient', 'code'], PatientReferencePath: 'subject' } } }
  },
  Routes: {
    terms: [
      ['ResourceType / Source', 'FHIR-ressurstype, for eksempel Observation, og ID-en til kilden som skal brukes. Source er en kilde-ID, ikke en URL.'],
      ['Code / Profile', 'Valgfrie, eksakte filtre. Code matcher code i søket, ofte som system|kode. Profile matcher søkeparameteren _profile, ikke populeringsprofilen. Utelat filtre du ikke trenger.'],
      ['Prioritet', 'En søkevariabelkobling går først. Deretter velges ruteregelen med flest samsvarende filtre, så en regel med bare ressurstype, og til slutt standardkilden. To like presise treff gir feil.']
    ],
    caption: 'Eksempel fra lokal demo: Observation går til demo, mens blodtrykk med denne koden går til demo-vitals. Bytt kilde-ID-ene ved bruk med egne kilder:',
    example: [{ ResourceType: 'Observation', Source: 'demo' }, { ResourceType: 'Observation', Code: 'http://loinc.org|85354-9', Source: 'demo-vitals' }]
  },
  QueryBindings: {
    terms: [
      ['Questionnaire / Version', 'Eksakt url og version fra skjemaet som inneholder søkevariabelen.'],
      ['Variable / Source', 'Navnet på en variable-extension med application/x-fhir-query, og kilde-ID-en dette søket skal sendes til. FHIRPath-uttrykk gjør ikke egne kildekall.'],
      ['LinkId', 'Utelat for en variabel på skjemaroten. For en variabel på et item: angi akkurat dette itemets linkId.']
    ],
    caption: 'Eksempel: hent bpBundle i det lokale svangerskapsskjemaet fra demo-vitals. Tilpass URL, versjon, variabelnavn og kilde-ID til ditt oppsett:',
    example: [{ Questionnaire: 'https://example.org/fhir/Questionnaire/pregnancy-demo', Version: '1.0.0', Variable: 'bpBundle', Source: 'demo-vitals' }]
  }
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
  endpointCheck?.setDisabled(value || !draft || dirty, dirty ? 'Lagre utkastet før du tester tilkoblingen.' : '');
}
function markDirty() { dirty = true; $('config-state').textContent = 'Ulagrede endringer'; message(''); endpointCheck?.setDisabled(true, 'Lagre utkastet før du tester tilkoblingen.'); }
function profileChoices() {
  return [...draft.Sources.filter(s => s.ExposeAsProfile !== false).map(s => [s.Id, `${s.Name} · enkeltkilde`]),
    ...draft.PopulationProfiles.map(p => [p.Id, p.Name])];
}
function choices(definition) {
  if (definition.type === 'profile') return profileChoices();
  if (definition.type.includes('source')) return draft.Sources.map(s => [s.Id, s.Name]);
  return definition.values.map(v => Array.isArray(v) ? v : [v, v]);
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
  endpointCheck?.destroy(); endpointCheck = null;
  $('config-endpoint-status').replaceChildren();
  $('config-endpoint-check').hidden = kind !== 'Sources' || !item;
  if (kind === 'Sources' && item) endpointCheck = new EndpointStatusControl($('config-endpoint-status'), item.Id, revision);
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
    const guide = jsonGuides[key];
    if (guide) {
      const details = node('details', 'config-field-guide');
      details.append(node('summary', '', 'Forklaring og eksempel: ' + label.replace(' · JSON', '')));
      const definitions = node('dl');
      for (const [term, explanation] of guide.terms) definitions.append(node('dt', '', term), node('dd', '', explanation));
      const code = node('pre'); code.append(node('code', '', pretty(guide.example)));
      details.append(definitions, node('p', '', guide.caption), code);
      group.append(details);
    }
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
function showJson() {
  if (busy || !draft) return;
  try {
    applyEditor();
    $('config-json-code').textContent = pretty(draft);
    $('config-json-description').textContent = dirty
      ? 'Viser hele utkastet, inkludert ulagrede endringer. Dette er kun en visning; ingenting lagres.'
      : 'Viser hele konfigurasjonen som er lastet på siden. Dette er kun en visning; ingenting lagres.';
    $('config-json-dialog').showModal();
    $('config-json-content').scrollTop = 0;
    document.body.classList.add('config-modal-open');
  } catch (error) { message(error.message, true); }
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
$('config-preview').addEventListener('click', showJson);
$('config-json-close').addEventListener('click', () => $('config-json-dialog').close());
$('config-json-dialog').addEventListener('close', () => document.body.classList.remove('config-modal-open'));
$('config-json-dialog').addEventListener('keydown', event => {
  if (event.key !== 'Tab') return;
  const first = $('config-json-close'), last = $('config-json-content');
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
});
$('config-export').addEventListener('click', () => {
  try {
    applyEditor(); const url = URL.createObjectURL(new Blob([pretty(draft)], { type: 'application/json' }));
    const link = node('a'); link.href = url; link.download = 'fhir-configuration.json'; document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (error) { message(error.message, true); }
});
window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
reload();
