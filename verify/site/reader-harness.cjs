const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../site/app.js'), 'utf8');
// Exercise the actual reader, renderer, event handlers and storage with a
// minimal DOM boundary and a small bilingual book; no dependencies are needed.
function boot({ en, uk, storage = {}, blockedStorage = false, hash = '#/chapter-1-example',
               browserLanguage = 'en-US', ukUnavailable = false } = {}) {
  class Element {
    constructor() {
      this.innerHTML = ''; this.textContent = ''; this.children = []; this.listeners = {};
      this.value = ''; this.hidden = true; this.dataset = {}; this.attributes = {};
      this.classList = { toggle() {}, remove() {}, add() {} };
      this.style = { setProperty() {} };
    }
    addEventListener(name, fn) { this.listeners[name] = fn; }
    setAttribute(name, value) { this.attributes[name] = value; }
    getAttribute(name) { return this.attributes[name]; }
    querySelectorAll() { return []; }
    querySelector() { return null; }
    appendChild(child) { this.children.push(child); }
    getBoundingClientRect() { return { top: 140, bottom: 200 }; }
  }
  const elements = {};
  const document = {
    documentElement: Object.assign(new Element(), { scrollHeight: 1000, clientHeight: 500, scrollTop: 0 }),
    getElementById(id) { return elements[id] ||= new Element(); },
    createElement() { return new Element(); }, addEventListener() {},
    // index.html ships only content.js; app.js adds a <script> for another edition on demand.
    head: { appendChild(script) {
      loaded.push(script.src);
      if (ukUnavailable || script.src !== 'content.uk.js') return script.onerror();
      window.BOOK_UK = structuredClone(uk); script.onload();
    } }
  };
  const loaded = [];
  const window = {
    BOOK: structuredClone(en), ALIASES: { legacy: 'chapter-1-example' },
    listeners: {}, pageYOffset: 0,
    addEventListener(name, fn) { this.listeners[name] = fn; },
    scrollTo(x, y) { this.pageYOffset = y; document.documentElement.scrollTop = y; }
  };
  const localStorage = {
    getItem(key) { if (blockedStorage) throw Error('blocked'); return storage[key] ?? null; },
    setItem(key, value) { if (blockedStorage) throw Error('blocked'); storage[key] = value; },
    removeItem(key) { if (blockedStorage) throw Error('blocked'); delete storage[key]; }
  };
  const location = { hash };
  const context = vm.createContext({ window, document, localStorage, location,
    history: { replaceState() {} }, navigator: { language: browserLanguage }, setTimeout() {}, clearTimeout() {} });
  vm.runInContext(source, context, { timeout: 5000 });
  function switchTo(language) {
    const toggle = elements.languageToggle;
    toggle.checked = language === 'uk';
    vm.runInContext('document.getElementById("languageToggle").listeners.change.call(document.getElementById("languageToggle"))', context, { timeout: 5000 });
  }
  function navigate(slug) {
    location.hash='#/'+slug;
    vm.runInContext('window.listeners.hashchange()', context, { timeout: 5000 });
  }
  return { elements, document, window, storage, location, loaded, switchTo, navigate };
}

module.exports = { boot };
