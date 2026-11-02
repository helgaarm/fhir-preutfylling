// Exercise the real upload handler and preview functions with a minimal DOM double.
// Optional Q/QR paths also exercise the delivered artifact without external calls.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/GenericPopulation/wwwroot/app.js', 'utf8');
class Node {
  constructor(tag) { this.tag = tag; this.children = []; this.textContent = ''; this.value = ''; this.handlers = {}; }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children = nodes; }
  addEventListener(name, callback) { this.handlers[name] = callback; }
}
const nodes = new Map();
const get = id => { if (!nodes.has(id)) nodes.set(id, new Node(id)); return nodes.get(id); };
const context = vm.createContext({ document: { createElement: tag => new Node(tag) }, $: get,
  selectTab() {}, busy: false, inputRevision: 0, envelope: null, qr: null,
  invalidate() { context.inputRevision++; }, setBusy(value) { context.busy = value; },
  updateInfo() {}, message(text, error) { if (error) throw new Error(text); } });
const declarations = ['flatten', 'flattenResponse', 'isHidden', 'visibleQuestions', 'pretty'].map(name =>
  source.split('\n').find(line => line.startsWith(`const ${name} =`)));
const functions = ['element', 'answerText', 'renderItems', 'showResult'].map(name => {
  const start = source.indexOf(`function ${name}(`);
  return source.slice(start, source.indexOf('\n}', start) + 2);
});
vm.runInContext([...declarations, ...functions].join('\n'), context);
const hidden = [{ url: 'http://hl7.org/fhir/StructureDefinition/questionnaire-hidden', valueBoolean: true }];
const q = { item: [{ linkId: 'parent', type: 'choice', text: 'Forelder', item: [
  { linkId: 'child', type: 'string', text: 'Oppfølging' },
  { linkId: 'metadata', type: 'string', extension: hidden } ] },
  { linkId: 'false', type: 'boolean', text: 'Nei-svar' },
  { linkId: 'hidden-group', type: 'group', extension: hidden, item: [{ linkId: 'hidden-child', type: 'string' }] }] };
context.qr = { item: [{ linkId: 'parent', text: 'Forelder', answer: [{ valueCoding: { code: '1', display: 'Ja' }, item: [
  { linkId: 'child', text: 'Oppfølging', answer: [{ valueString: 'Detalj' }] },
  { linkId: 'metadata', answer: [{ valueString: 'must-not-show' }] }] }] },
  { linkId: 'false', text: 'Nei-svar', answer: [{ valueBoolean: false }] },
  { linkId: 'hidden-group', item: [{ linkId: 'hidden-child', answer: [{ valueString: 'also-hidden' }] }] }] };
context.envelope = { parameter: [{ name: 'issues', resource: { issue: [] } }] };
context.showResult(q, 10, '2');
const texts = node => [node.textContent, ...node.children.flatMap(texts)];
assert.equal(get('answered').textContent, '3/3');
assert(texts(get('preview')).includes('Ja'));
assert(texts(get('preview')).includes('Detalj'));
assert(texts(get('preview')).includes('Nei (false)'));
assert(!texts(get('preview')).includes('must-not-show'));
assert(!texts(get('preview')).includes('also-hidden'));
assert.equal(context.answerText({ valueCoding: { code: 'fallback' } }), 'fallback');
assert.equal(context.answerText({ valueInteger: 0 }), '0');
q.item[0].repeats = true;
context.qr.item[0].answer.push({ valueCoding: { code: '2', display: 'Andre valg' } });
context.showResult(q, 10, '2');
assert(texts(get('preview')).includes('Ja\nAndre valg'));
assert.equal(get('answered').textContent, '3/3');
const start = source.indexOf("$('file').addEventListener('change'");
vm.runInContext(source.slice(start, source.indexOf('\n});', start) + 4), context);
(async () => {
  const actualQ = process.argv[2] ? fs.readFileSync(process.argv[2], 'utf8') : JSON.stringify(q);
  get('file').files = [{ size: Buffer.byteLength(actualQ), text: async () => actualQ }];
  const uploadQ = process.argv[2] ? JSON.parse(actualQ) : { ...q, resourceType: 'Questionnaire' };
  if (!process.argv[2]) get('file').files[0].text = async () => JSON.stringify(uploadQ);
  await get('file').handlers.change();
  assert.deepEqual(JSON.parse(get('questionnaire').value), uploadQ);
  assert.equal(get('example').value, 'custom');
  assert.equal(context.busy, false);
  if (process.argv[3]) {
    context.qr = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
    context.showResult(uploadQ, 10, '2');
    assert(texts(get('preview')).includes('Er du i jobb?'));
    assert(texts(get('preview')).includes('Ja'));
    const medical = vm.runInContext('flatten', context)(uploadQ.item).find(i => i.linkId === '5628da84-f498-41c4-8d7b-c3c6d20361f6');
    if (medical?.repeats === true) {
      const item = vm.runInContext('flattenResponse', context)(context.qr.item).find(i => i.linkId === medical.linkId);
      assert.equal(item.answer.length, 2);
      assert(texts(get('preview')).includes(item.answer.map(context.answerText).join('\n')));
      assert.equal(texts(get('preview')).filter(text => text === medical.text).length, 1);
    }
    console.log('Delivered Q upload preserved every property; preview:', get('answered').textContent);
  }
  console.log('PASS: original upload, coded choices, nested answers, false/zero, hidden fields and hidden ancestors');
})().catch(error => { console.error(error); process.exitCode = 1; });
