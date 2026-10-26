const test = require('node:test');
const assert = require('node:assert/strict');

const en = [
  { id: '101-example', slug: 'chapter-1-example', title: 'Chapter 1: Example', nav: 'Example',
    num: '1', part: 'Part 1', md: '# Chapter 1: Example\n\n## A section\n\nEnglish prose.\n\n```csharp\nvar x = 1;\n```' },
  { id: '999-whats-new', slug: 'whats-new', title: "What's New", nav: "What's New", num: '', part: "What's New",
    md: '# What\'s New\n\n## Release — October 9, 2026\n\nNew content: [Example](#chapter-1-example).' }
];
const uk = [
  { ...en[0], title: 'Розділ 1: Приклад', nav: 'Приклад', part: 'Частина 1',
    md: '# Розділ 1: Приклад\n\n## Секція\n\nУкраїнський текст.\n\n```csharp\nvar x = 1;\n```',
    anchors: ['chapter-1-example', 'a-section'] },
  { ...en[1], title: 'Що нового', nav: 'Що нового', part: 'Що нового',
    md: '# Що нового\n\n## Випуск — 9 жовтня 2026\n\nНовий текст: [Приклад](#chapter-1-example).',
    anchors: ['whats-new', 'release-october-9-2026'] }
];

const { boot: bootReader } = require('./reader-harness.cjs');
// Most cases start from a Ukrainian browser; the default-language tests below set it explicitly.
function boot(options={}) { return bootReader({ browserLanguage: 'uk-UA', ...options, en, uk }); }

test('an English browser gets English and never downloads the Ukrainian bundle', () => {
  const app = boot({ browserLanguage: 'en-GB' });
  assert.equal(app.document.documentElement.lang, 'en');
  assert.equal(app.elements.languageToggle.checked, false);
  assert.match(app.elements.content.innerHTML, /English prose/);
  assert.deepEqual(app.loaded, []);
  app.switchTo('uk');
  assert.deepEqual(app.loaded, ['content.uk.js']);
  assert.match(app.elements.content.innerHTML, /Український текст/);
});

test('a saved choice wins over the browser language', () => {
  assert.equal(boot({ storage: { site_lang: 'en' } }).document.documentElement.lang, 'en');
  assert.equal(boot({ browserLanguage: 'en-US', storage: { site_lang: 'uk' } }).document.documentElement.lang, 'uk');
});

test('if the Ukrainian bundle cannot load, the reader stays usable in English', () => {
  const app = boot({ ukUnavailable: true });
  assert.equal(app.document.documentElement.lang, 'en');
  assert.match(app.elements.content.innerHTML, /English prose/);
  app.switchTo('uk');
  assert.equal(app.elements.languageToggle.checked, false);
  assert.equal(app.storage.site_lang, undefined);
});

test('a Ukrainian browser gets the Ukrainian shell, prose and buttons while retaining section anchors', () => {
  const app = boot();
  assert.equal(app.document.documentElement.lang, 'uk');
  assert.equal(app.elements.languageToggle.checked, true);
  assert.match(app.elements.content.innerHTML, /Український текст/);
  assert.match(app.elements.content.innerHTML, /id="a-section"/);
  assert.match(app.elements.content.innerHTML, /Копіювати/);
  assert.match(app.elements.nav.innerHTML, /Приклад/);
  assert.equal(app.elements.search.placeholder, 'Шукати розділи…');
  assert.match(app.elements.wnBody.innerHTML, /Новий текст/);
});

test('switching preserves route, progress and release identity and persists the choice', () => {
  const app = boot({ storage: { 'pos:chapter-1-example': '{"p":70,"d":0,"i":0}' } });
  const seen = app.storage.wn_seen;
  app.switchTo('en');
  assert.equal(app.document.documentElement.lang, 'en');
  assert.equal(app.location.hash, '#/chapter-1-example');
  assert.equal(app.storage.site_lang, 'en');
  assert.equal(JSON.parse(app.storage['pos:chapter-1-example']).p, 70);
  assert.equal(app.storage.wn_seen, seen);
  assert.match(app.elements.content.innerHTML, /English prose/);
  assert.match(app.elements.nav.innerHTML, /Example/);
  assert.match(app.elements.wnBody.innerHTML, /New content/);
  assert.equal(boot({ storage: app.storage }).document.documentElement.lang, 'en');
  app.switchTo('uk');
  assert.match(app.elements.content.innerHTML, /id="a-section"/);
  assert.equal(app.storage.wn_seen, seen);
});

test('search follows the active language and localizes empty results', () => {
  const app = boot();
  app.elements.search.value = 'Український';
  app.elements.search.listeners.input.call(app.elements.search);
  assert.match(app.elements.searchResults.innerHTML, /Приклад/);
  app.switchTo('en');
  assert.match(app.elements.searchResults.innerHTML, /No results/);
  app.elements.search.value = 'English';
  app.elements.search.listeners.input.call(app.elements.search);
  assert.match(app.elements.searchResults.innerHTML, /Example/);
});

test('legacy chapter links and switching work even when localStorage is unavailable', () => {
  const app = boot({ hash: '#/legacy', blockedStorage: true });
  assert.match(app.elements.content.innerHTML, /Український текст/);
  app.switchTo('en');
  assert.match(app.elements.content.innerHTML, /English prose/);
  assert.equal(app.document.documentElement.lang, 'en');
});

test('reading settings follow the language and are applied, shown and remembered', () => {
  const app = boot();
  const root = app.document.documentElement;
  assert.equal(app.elements.readerBtn.title, 'Налаштування читання');
  assert.equal(app.elements['theme-paper'].textContent, 'Папір');
  assert.equal(app.elements['font-serif'].textContent, 'Із засічками');
  assert.equal(root.getAttribute('data-theme'), 'auto');
  app.elements['theme-paper'].listeners.click();
  app.elements['font-serif'].listeners.click();
  app.elements.sizeUp.listeners.click();
  assert.equal(root.getAttribute('data-theme'), 'paper');
  assert.equal(root.getAttribute('data-font'), 'serif');
  assert.equal(root.style['--fs-scale'], '1.1');
  assert.equal(app.elements.sizeValue.textContent, '110%');
  assert.equal(app.elements['theme-paper'].getAttribute('aria-pressed'), 'true');
  assert.equal(app.elements['theme-auto'].getAttribute('aria-pressed'), 'false');
  app.switchTo('en');
  assert.equal(app.elements.readerBtn.title, 'Reading settings');
  assert.equal(app.elements['theme-paper'].textContent, 'Paper');
  const again = boot({ storage: app.storage }).document.documentElement;
  assert.equal(again.getAttribute('data-theme'), 'paper');
  assert.equal(again.getAttribute('data-font'), 'serif');
  assert.equal(again.style['--fs-scale'], '1.1');
});

test('text size stays within its steps and unknown stored settings fall back to the defaults', () => {
  const app = boot({ storage: { theme: 'neon', font: 'comic', text_size: '7' } });
  const root = app.document.documentElement;
  assert.equal(root.getAttribute('data-theme'), 'auto');
  assert.equal(root.getAttribute('data-font'), 'sans');
  assert.equal(root.style['--fs-scale'], '1');
  for (let i = 0; i < 12; i++) app.elements.sizeUp.listeners.click();
  assert.equal(app.elements.sizeValue.textContent, '160%');
  assert.equal(app.elements.sizeUp.disabled, true);
  for (let i = 0; i < 12; i++) app.elements.sizeDown.listeners.click();
  assert.equal(app.elements.sizeValue.textContent, '85%');
  assert.equal(app.elements.sizeDown.disabled, true);
  assert.equal(app.storage.text_size, '0.85');
});

test('reading settings still change the page when localStorage is unavailable', () => {
  const app = boot({ blockedStorage: true });
  app.elements['theme-dark'].listeners.click();
  app.elements.sizeDown.listeners.click();
  assert.equal(app.document.documentElement.getAttribute('data-theme'), 'dark');
  assert.equal(app.document.documentElement.style['--fs-scale'], '0.9');
});

test('an isolated pipe row renders as prose and does not freeze chapter switching', () => {
  const oddEn = structuredClone(en), oddUk = structuredClone(uk);
  oddEn[0].md += '\n\n| orphan row | no separator |\n';
  oddUk[0].md += '\n\n| окремий рядок | без роздільника |\n';
  const app = bootReader({ en: oddEn, uk: oddUk, browserLanguage: 'uk-UA' });
  assert.match(app.elements.content.innerHTML, /окремий рядок/);
  app.switchTo('en');
  assert.match(app.elements.content.innerHTML, /orphan row/);
});
