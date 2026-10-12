// Dependency-free production-script regressions: node tests/ForegroundWatchTests.js
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.join(__dirname, '../src/Luma/Services/ForegroundWatchService.cs'), 'utf8');
const matching = source.match(/private const string MatchingScript = """\r?\n([\s\S]*?)^\s*""";/m);
assert.ok(matching, 'Production MatchingScript raw string must exist');
const templates = [...source.matchAll(/var script = \$\$"""\r?\n([\s\S]*?)^\s*""";/gm)].map(match => match[1]);
assert.equal(templates.length, 2, 'Expected exactly the observation and effect raw script templates');
assert.ok(!templates[0].includes('el.click();'), 'First template must observe only');
assert.ok(templates[1].includes('el.click();'), 'Second template must contain the effect');

function scriptFor(effect, text, observed = {}) {
  const substitutions = new Map([
    ['MatchingScript', matching[1]],
    ['JsonSerializer.Serialize(text)', JSON.stringify(text)],
    ['JsonSerializer.Serialize(observed.Selector)', JSON.stringify(observed.selector ?? null)],
    ['JsonSerializer.Serialize(observed.Source)', JSON.stringify(observed.source ?? null)],
    ['JsonSerializer.Serialize(observed.DocumentId)', JSON.stringify(observed.documentId ?? null)]
  ]);
  // Replace only C# interpolation holes. Matching, eligibility, selection, and effects
  // are executed verbatim from the production file, never reimplemented here.
  return templates[effect ? 1 : 0].replace(/\{\{([^{}]+)\}\}/g, (_, expression) => {
    assert.ok(substitutions.has(expression.trim()), `Unsupported C# interpolation: ${expression}`);
    return substitutions.get(expression.trim());
  });
}

class Element {
  constructor(text = 'Go', attributes = {}, tag = 'button') {
    this.innerText = text; this.attributes = attributes; this.localName = tag;
    this.id = attributes.id ?? ''; this.isConnected = true;
    this.parentElement = null; this.previousElementSibling = null; this.children = [];
    this.style = {display: 'block', visibility: 'visible', opacity: '1', pointerEvents: 'auto', ...attributes.style};
    this.rect = {left: 10, top: 10, right: 110, bottom: 40};
    this.clickCount = 0;
  }
  getAttribute(key) { return this.attributes[key] ?? null; }
  matches(selector) {
    if (selector === ':disabled') {
      if (this.attributes.disabled) return true;
      for (let node = this.parentElement; node; node = node.parentElement) {
        if (node.localName === 'fieldset' && node.attributes.disabled) return true;
      }
      return false;
    }
    if (selector === 'button') return this.localName === 'button';
    if (selector === 'a[href]') return this.localName === 'a' && this.getAttribute('href') !== null;
    const role = selector.match(/^\[role="(button|link)"\]$/);
    if (role) return this.getAttribute('role') === role[1];
    const input = selector.match(/^input\[type="(button|submit|reset)"\]$/);
    if (input) return this.localName === 'input' && this.getAttribute('type') === input[1];
    throw new Error(`Unsupported candidate selector in DOM double: ${selector}`);
  }
  closest(selector) {
    assert.equal(selector, '[inert]');
    for (let node = this; node; node = node.parentElement) {
      if (Object.hasOwn(node.attributes, 'inert')) return node;
    }
    return null;
  }
  getClientRects() { return [this.rect]; }
  contains(target) {
    for (let node = target; node; node = node.parentElement) if (node === this) return true;
    return false;
  }
  click() { this.clickCount++; }
}
class Input extends Element {
  constructor(value, attributes = {}) {
    super('', {type: 'button', ...attributes}, 'input'); this.value = value;
  }
}
const cssEscape = value => String(value).replace(/[^A-Za-z0-9_-]/g, ch => '\\' + ch.codePointAt(0).toString(16) + ' ');
const cssUnescape = value => value.replace(/\\([a-fA-F0-9]+)\s?/g, (_, hex) => String.fromCodePoint(parseInt(hex, 16)));

function fixture(elements = [new Element('Go', {id: 'go'})]) {
  const root = new Element('', {}, 'html'), body = new Element('', {}, 'body');
  root.rect = body.rect = {left: 0, top: 0, right: 0, bottom: 0};
  body.parentElement = root; root.children = [body]; body.children = elements;
  elements.forEach((el, index) => {
    el.parentElement = body; el.previousElementSibling = elements[index - 1] ?? null;
    el.rect = {left: 10 + index * 120, top: 10, right: 110 + index * 120, bottom: 40};
  });
  const nodes = [root, body, ...elements];
  const fullPath = el => {
    const parts = [];
    for (let node = el; node; node = node.parentElement) {
      parts.unshift(cssEscape(node.localName) + ':nth-child(' + (node.parentElement ? node.parentElement.children.indexOf(node) + 1 : 1) + ')');
    }
    return parts.join(' > ');
  };
  const state = {elements, root, body, overlay: null, queryCount: 0};
  const document = {
    querySelectorAll(selector) {
      state.queryCount++;
      if (selector.startsWith('#')) return nodes.filter(el => el.id === cssUnescape(selector.slice(1)));
      if (selector.includes(':nth-child(')) return nodes.filter(el => fullPath(el) === selector);
      const selectors = selector.split(',');
      return nodes.filter(el => selectors.some(part => el.matches(part)));
    },
    elementFromPoint(x, y) {
      return state.overlay ?? [...elements].reverse().find(el =>
        x >= el.rect.left && x < el.rect.right && y >= el.rect.top && y < el.rect.bottom) ?? null;
    }
  };
  state.environment = {
    document, location: {href: 'https://example.test/watch'}, performance: {timeOrigin: 12345},
    HTMLElement: Element, HTMLInputElement: Input, CSS: {escape: cssEscape},
    getComputedStyle: el => el.style, innerWidth: 1200, innerHeight: 800
  };
  return state;
}
const observe = (state, text = 'Go') => vm.runInNewContext(scriptFor(false, text), state.environment, {timeout: 1000});
const click = (state, approved, text = 'Go') => vm.runInNewContext(scriptFor(true, text, approved), state.environment, {timeout: 1000});
const totalClicks = state => state.elements.reduce((count, el) => count + el.clickCount, 0);
let passed = 0;
function test(name, body) {
  try { body(); passed++; }
  catch (error) { error.message = `${name}: ${error.message}`; throw error; }
}
function refuseEffect(modify, bindingModify = () => {}) {
  const state = fixture(), approved = observe(state);
  assert.equal(approved.found, true);
  modify(state); bindingModify(approved);
  const result = click(state, approved);
  assert.equal(result.clicked, false); assert.ok(result.error);
  assert.equal(totalClicks(state), 0);
  return {state, result};
}
function refuseTarget(elements, modify = () => {}) {
  const state = fixture(elements); modify(state);
  const result = observe(state);
  assert.equal(result.found, false); assert.equal(totalClicks(state), 0);
  // Also exercise effect-time eligibility independently of C# observation rejection.
  const effectResult = click(state, {selector: '#go', source: state.environment.location.href, documentId: '12345'});
  assert.equal(effectResult.clicked, false); assert.equal(totalClicks(state), 0);
  return result;
}

test('observation emits document binding without clicking', () => {
  const state = fixture(), result = observe(state);
  assert.equal(result.found, true); assert.equal(result.clicked, false);
  assert.equal(result.selector, '#go'); assert.equal(result.source, state.environment.location.href);
  assert.equal(result.documentId, '12345'); assert.equal(result.matchCount, 1);
  assert.equal(totalClicks(state), 0);
});
test('unchanged approved tuple clicks once', () => {
  const state = fixture(), approved = observe(state), result = click(state, approved);
  assert.equal(result.clicked, true); assert.equal(totalClicks(state), 1);
  for (const key of ['selector', 'source', 'documentId']) assert.equal(result[key], approved[key]);
});
test('different href rejects before querying targets', () => {
  const {state} = refuseEffect(state => { state.environment.location.href = 'https://other.test/watch'; state.queryCount = 0; });
  assert.equal(state.queryCount, 0);
});
test('same origin different href rejects', () => refuseEffect(state => { state.environment.location.href = 'https://example.test/other'; }));
test('same href new document rejects before querying targets', () => {
  const {state} = refuseEffect(state => { state.environment.performance.timeOrigin++; state.queryCount = 0; });
  assert.equal(state.queryCount, 0);
});
for (const key of ['selector', 'source', 'documentId']) {
  for (const missing of [null, '']) {
    test(`missing expected ${key} (${JSON.stringify(missing)}) rejects`, () => refuseEffect(() => {}, approved => { approved[key] = missing; }));
  }
}
test('document identifier equality remains ordinal', () => refuseEffect(() => {}, approved => { approved.documentId = '012345'; }));
test('same exact text with changed selector rejects', () => refuseEffect(state => { state.elements[0].id = 'replacement'; }));
test('duplicate eligible text rejects', () => {
  const result = refuseTarget([new Element('Go', {id: 'go'}), new Element('Go', {id: 'other'})]);
  assert.equal(result.matchCount, 2); assert.match(result.error, /ambiguous/i);
});
test('duplicate introduced after approval rejects', () => {
  const state = fixture(), approved = observe(state), second = new Element('Go', {id: 'other'});
  const changed = fixture([state.elements[0], second]);
  assert.equal(click(changed, approved).clicked, false); assert.equal(totalClicks(changed), 0);
});
test('disabled target rejects', () => refuseTarget([new Element('Go', {id: 'go', disabled: true})]));
test('disabled fieldset ancestor rejects', () => refuseTarget([new Element('Go', {id: 'go'})], state => { state.body.localName = 'fieldset'; state.body.attributes.disabled = true; }));
test('aria-disabled ancestor rejects', () => refuseTarget([new Element('Go', {id: 'go'})], state => { state.body.attributes['aria-disabled'] = 'TRUE'; }));
test('inert ancestor rejects', () => refuseTarget([new Element('Go', {id: 'go'})], state => { state.body.attributes.inert = ''; }));
test('hidden target rejects', () => refuseTarget([new Element('Go', {id: 'go', style: {visibility: 'hidden'}})]));
test('display-none ancestor rejects', () => refuseTarget([new Element('Go', {id: 'go'})], state => { state.body.style.display = 'none'; }));
test('transparent ancestor rejects', () => refuseTarget([new Element('Go', {id: 'go'})], state => { state.body.style.opacity = '0'; }));
test('pointer-events-none rejects', () => refuseTarget([new Element('Go', {id: 'go', style: {pointerEvents: 'none'}})]));
test('obscured target rejects', () => refuseTarget([new Element('Go', {id: 'go'})], state => { state.overlay = new Element('overlay', {}, 'span'); }));
test('offscreen target rejects', () => refuseTarget([new Element('Go', {id: 'go'})], state => { state.elements[0].rect = {left: 10, top: 900, right: 110, bottom: 930}; }));
test('disconnected target rejects', () => refuseTarget([new Element('Go', {id: 'go'})], state => { state.elements[0].isConnected = false; }));
test('substring text rejects', () => refuseTarget([new Element('Go now', {id: 'go'})]));
test('case-different text rejects', () => refuseTarget([new Element('go', {id: 'go'})]));
test('whitespace normalization preserves exact equality', () => {
  const state = fixture([new Element('  Go\t now\n', {id: 'go'})]), approved = observe(state, '\tGo\u00a0now ');
  assert.equal(approved.matchedText, 'Go now'); assert.equal(click(state, approved, 'Go  now').clicked, true);
});
test('input value matching', () => {
  const state = fixture([new Input('Go', {id: 'go'})]); assert.equal(click(state, observe(state)).clicked, true);
});
test('aria-label fallback', () => {
  const state = fixture([new Element('', {id: 'go', 'aria-label': ' Go '})]); assert.equal(observe(state).found, true);
});
test('duplicate ids use a verified full path', () => {
  const state = fixture([new Element('Go', {id: 'go'}), new Element('other', {id: 'go'}, 'span')]);
  const approved = observe(state);
  assert.equal(approved.selector, 'html:nth-child(1) > body:nth-child(1) > button:nth-child(1)');
  assert.equal(click(state, approved).clicked, true);
});
test('duplicate approved id introduced after approval rejects', () => {
  const state = fixture(), approved = observe(state);
  const changed = fixture([state.elements[0], new Element('other', {id: 'go'}, 'span')]);
  assert.equal(click(changed, approved).clicked, false); assert.equal(totalClicks(changed), 0);
});
test('escaped unique id remains bound', () => {
  const state = fixture([new Element('Go', {id: 'go:action'})]); assert.equal(click(state, observe(state)).clicked, true);
});
test('serialized text remains data, including quotes and C#-like holes', () => {
  const text = 'Go "quoted" \\ {{MatchingScript}}';
  const state = fixture([new Element(text, {id: 'go'})]), approved = observe(state, text);
  assert.equal(click(state, approved, text).clicked, true);
});
console.log(`Foreground watcher JavaScript: ${passed} production-script document/selector/text/eligibility regression cases passed.`);
