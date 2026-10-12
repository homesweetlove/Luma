// Run after VaultTests.RunAsync with LUMA_VAULT_TEST_SCRIPT_DIRECTORY set to an existing directory.
// node tests/VaultAutofillTests.js <that-directory>
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const login = fs.readFileSync(path.join(process.argv[2], 'login-fill.js'), 'utf8');
const payment = fs.readFileSync(path.join(process.argv[2], 'payment-fill.js'), 'utf8');
let passed = 0;
function run(script, modify = () => {}) {
  const writes = [];
  class Input {
    constructor(field, type = 'text') {
      this.field = field; this.type = type; this.name = field; this.id = ''; this.labels = [];
      this.form = { action:'https://example.com/login', method:'post' };
      this.parentElement = null; this.style = { visibility:'visible',display:'block',opacity:'1' };
      this.rect = { width:100,height:20,top:10,left:10,bottom:30,right:110 };
    }
    getAttribute(key) { return key === 'autocomplete' ? this.field : null; }
    getBoundingClientRect() { return this.rect; }
    matches() { return !!(this.disabled || this.parentElement?.disabledFieldset); }
    closest() { return this.hidden ? this : null; }
    set value(value) { this.stored = value; writes.push({ field:this.field,value }); }
    get value() { return this.stored; }
    dispatchEvent() { throw Error('Autofill must not dispatch events'); }
    click() { throw Error('Autofill must not click'); }
  }
  class Textarea extends Input { }
  class Select extends Input { }
  const fields = script === login ? [new Input('username'),new Input('current-password','password')]
    : [new Input('cc-name'),new Input('cc-exp-month','number'),new Input('cc-exp-year','number'),new Input('cc-number'),new Input('cc-csc')];
  const window = { key:'token',innerHeight:800,innerWidth:1200 }; window.top = window;
  const location = { protocol:'https:',origin:'https://example.com',href:'https://example.com/' };
  const document = { visibilityState:'visible',hasFocus:() => true,querySelectorAll:() => fields };
  const environment = {window,location,document,HTMLInputElement:Input,HTMLTextAreaElement:Textarea,HTMLSelectElement:Select,
    getComputedStyle:el => el.style,URL};
  modify({fields,window,location,document,Input});
  const result = vm.runInNewContext(script, environment);
  return {result,writes,environment};
}
function refuse(modify) { const r = run(login, modify); assert.equal(r.result.ok,false); assert.equal(r.writes.length,0); passed++; }
let r = run(login); assert.equal(r.result.ok,true); assert.equal(r.writes.length,2); passed++;
refuse(({location}) => location.origin = 'https://evil.test');
refuse(({location}) => location.protocol = 'http:');
refuse(({window}) => window.key = 'stale-document');
refuse(({window}) => window.top = {});
refuse(({document}) => document.hasFocus = () => false);
refuse(({document}) => document.visibilityState = 'hidden');
refuse(({fields,Input}) => fields.push(new Input('username')));
refuse(({fields}) => fields[1].parentElement = { parentElement:null,style:{visibility:'visible',display:'block',opacity:'0'} });
refuse(({fields}) => fields[1].rect = { width:100,height:20,top:900,left:10,bottom:920,right:110 });
refuse(({fields}) => fields[1].parentElement = { disabledFieldset:true,parentElement:null,style:{visibility:'visible',display:'block',opacity:'1'} });
refuse(({fields}) => fields[1].name = 'cvv');
refuse(({fields}) => fields[1].form.action = 'https://evil.test/collect');
refuse(({fields}) => fields[1].form.method = 'get');
refuse(({fields}) => fields[1].type = 'text');
r = run(payment); assert.equal(r.result.ok,true); assert.equal(r.writes.length,3);
assert.deepEqual(r.writes.map(w => w.field), ['cc-name','cc-exp-month','cc-exp-year']); passed++;
assert.equal(vm.runInNewContext(login, r.environment).ok,false); passed++;
console.log(`Vault autofill JavaScript: ${passed} origin/document/field/visibility/payment regression cases passed.`);
