// Parse the JavaScript embedded in the C# home page and check safe local favorites.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../src/Luma/Services/HomeDashboardService.cs'), 'utf8');
const raw = source.match(/private const string HomeHtml = """([\s\S]*?)""";/);
assert(raw, 'Home HTML raw string is missing.');
const inline = raw[1].match(/<script>\s*([\s\S]*?)\s*<\/script>/i);
assert(inline, 'Home inline script is missing.');
const script = inline[1];
new vm.Script(script, { filename: 'LumaHome.inline.js' });

const validator = script.match(/  const checkedFavoriteUrl = raw => \{[\s\S]*?\n  \};/);
assert(validator, 'Favorite URL validator not found.');
const checkedFavoriteUrl = vm.runInNewContext(
  '(() => { ' + validator[0] + '; return checkedFavoriteUrl; })()',
  { URL }
);
assert.equal(checkedFavoriteUrl('https://example.com/path'), 'https://example.com/path');
assert.equal(checkedFavoriteUrl('example.com'), 'https://example.com/');
for (const bad of [
  'javascript:alert(1)', 'file:///C:/secret.txt', 'https://alice:password@example.com/',
  'https://example.com/\n', 'https://example.com\\@evil.com'
]) assert.equal(checkedFavoriteUrl(bad), null, 'Unsafe favorite accepted: ' + bad);

const loader = script.match(/  function loadFavorites\(\) \{[\s\S]*?\n  \}/);
assert(loader, 'Favorites loader missing.');
const storage = {
  getItem() {
    return JSON.stringify([
      { name: 'Bad', url: 'javascript:alert(1)' },
      { name: 'Credentials', url: 'https://me:secret@example.com' },
      { name: 'Good', url: 'https://example.com/page' },
      { name: 123, url: 'https://example.com/' }
    ]);
  }
};
const loadFavorites = vm.runInNewContext(
  '(() => { const DEFAULT_FAVORITES = []; ' + validator[0] + loader[0] + '; return loadFavorites; })()',
  { URL, localStorage: storage }
);
const restored = loadFavorites();
assert.equal(restored.length, 1);
assert.equal(restored[0].name, 'Good');
assert.equal(restored[0].url, 'https://example.com/page');
console.log('PASS: home script syntax and safe favorite URL loading');
