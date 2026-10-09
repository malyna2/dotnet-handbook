const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { boot } = require('./reader-harness.cjs');
const root = path.join(__dirname, '../..');
const context = { window: {} };
for (const name of ['content.js', 'content.uk.js']) {
  vm.runInNewContext(fs.readFileSync(path.join(root, 'site', name), 'utf8'), context);
}
const { BOOK: en, BOOK_UK: uk } = context.window;
const blocks = md => md.match(/^```[^\n]*\n[\s\S]*?^```[^\n]*$/gm) || [];

test('every page has a Ukrainian edition with the same route and code examples', () => {
  assert.equal(uk.length, en.length);
  en.forEach((chapter, index) => {
    const translated = uk[index];
    assert.equal(translated.id, chapter.id);
    assert.equal(translated.slug, chapter.slug);
    assert.match(translated.md, /[А-Яа-яІіЇїЄєҐґ]/);
    assert.deepEqual(Array.from(blocks(translated.md)), Array.from(blocks(chapter.md)), chapter.id);
    assert.equal(fs.existsSync(path.join(root, 'chapters/uk', `${chapter.id}.md`)), true);
  });
});

test('translated headings retain every English section anchor in order', () => {
  const app = boot({ en, uk, storage: { site_lang: 'en' } });
  const ids = () => Array.from(app.elements.content.innerHTML.matchAll(/<h[1-6] id="([^"]+)"/g), match => match[1]);
  en.forEach(chapter => {
    app.navigate(chapter.slug);
    const english = ids();
    app.switchTo('uk');
    assert.deepEqual(ids(), english, chapter.id);
    app.switchTo('en');
  });
});

test('both local editions load without a runtime translation service', () => {
  const html = fs.readFileSync(path.join(root, 'site/index.html'), 'utf8');
  const app = fs.readFileSync(path.join(root, 'site/app.js'), 'utf8');
  assert.match(html, /src="content.uk.js"/);
  assert.match(html, /id="languageToggle"/);
  assert.doesNotMatch(html + app, /MyMemory|mymemory\.translated|easyToggle|selTranslate|langModal/);
  assert.doesNotMatch(app, /\bfetch\s*\(/);
});
