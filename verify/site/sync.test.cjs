const test = require('node:test');
const assert = require('node:assert/strict');
const { boot: bootReader } = require('./reader-harness.cjs');

const en = [
  { id: '101-a', slug: 'chapter-1-a', title: 'Chapter 1: A', nav: 'A', num: '1', part: 'Part 1', md: '# Chapter 1: A\n\nText.' },
  { id: '102-b', slug: 'chapter-2-b', title: 'Chapter 2: B', nav: 'B', num: '2', part: 'Part 1', md: '# Chapter 2: B\n\nText.' },
  { id: '999-whats-new', slug: 'whats-new', title: "What's New", nav: "What's New", num: '', part: "What's New", md: '# What\'s New\n\nNothing.' }
];
const uk = en.map(c => ({ ...c, anchors: [c.slug] }));
const CONFIG = { url: 'https://example.supabase.co/', anonKey: 'sb_publishable_test' };

// An in-memory copy of supabase/schema.sql's sync_get / sync_put (same version rules).
function fakeServer({ failPuts = 0 } = {}) {
  const rows = new Map(); const calls = []; let fails = failPuts;
  const fetch = async (url, init) => {
    const name = url.split('/rpc/')[1]; const body = JSON.parse(init.body);
    calls.push({ name, url, headers: init.headers, body });
    if (name === 'sync_get') {
      const r = rows.get(body.p_key);
      return { ok: true, json: async () => r ? { data: structuredClone(r.data), version: r.version } : { data: {}, version: 0 } };
    }
    const r = rows.get(body.p_key);
    if (fails > 0) { fails--; return { ok: true, json: async () => ({ ok: false, data: r?.data || {}, version: r?.version || 0 }) }; }
    if ((r ? r.version : 0) !== body.p_version) return { ok: true, json: async () => ({ ok: false, data: r.data, version: r.version }) };
    const next = { data: structuredClone(body.p_data), version: (r ? r.version : 0) + 1 };
    rows.set(body.p_key, next);
    return { ok: true, json: async () => ({ ok: true, version: next.version }) };
  };
  return { fetch, rows, calls };
}
const device = (server, storage = {}) => bootReader({ en, uk, storage, sync: CONFIG, fetch: server.fetch });
const settle = async () => { for (let i = 0; i < 12; i++) await new Promise(r => setImmediate(r)); };
const click = (app, id) => app.elements[id].listeners.click();
const pos = (app, slug) => JSON.parse(app.storage['pos:' + slug] || 'null');

test('the sync row is hidden unless a Supabase project is configured', () => {
  assert.equal(bootReader({ en, uk }).elements.syncRow.hidden, true);
  assert.equal(device(fakeServer()).elements.syncRow.hidden, false);
});

test('turning sync on creates a 128-bit code and uploads local progress', async () => {
  const server = fakeServer();
  const a = device(server, { 'pos:chapter-1-a': '{"p":40,"d":0,"i":3}' });
  click(a, 'syncStart'); await settle();
  assert.match(a.storage.sync_code, /^[A-Z2-7]{26}$/);
  assert.equal(a.elements.syncOn.hidden, false);
  assert.equal(a.elements.syncOff.hidden, true);
  const row = server.rows.get(a.storage.sync_code);
  assert.equal(row.data['chapter-1-a'].p, 40);
  assert.equal(row.data['chapter-1-a'].i, 3);
  const get = server.calls[0];
  assert.equal(get.url, 'https://example.supabase.co/rest/v1/rpc/sync_get');
  assert.equal(get.headers.apikey, CONFIG.anonKey);
  assert.equal(get.headers.Authorization, undefined);   // publishable keys are not JWTs
});

test('two devices converge: the larger progress wins and finished stays finished', async () => {
  const server = fakeServer();
  const a = device(server, { 'pos:chapter-1-a': '{"p":40,"d":0,"i":3}', 'pos:chapter-2-b': '{"p":100,"d":1,"i":9}' });
  click(a, 'syncStart'); await settle();
  const code = a.storage.sync_code;
  const b = device(server, { 'pos:chapter-1-a': '{"p":70,"d":0,"i":8}', 'pos:chapter-2-b': '{"p":20,"d":0,"i":1}' });
  b.elements.syncInput.value = code.slice(0, 5).toLowerCase() + ' - ' + code.slice(5);   // typed by hand
  click(b, 'syncJoin'); await settle();
  assert.equal(b.storage.sync_code, code);
  assert.deepEqual([pos(b, 'chapter-1-a').p, pos(b, 'chapter-1-a').i], [70, 8]);
  assert.deepEqual([pos(b, 'chapter-2-b').p, pos(b, 'chapter-2-b').d], [100, 1]);
  click(a, 'syncNow'); await settle();
  assert.deepEqual([pos(a, 'chapter-1-a').p, pos(a, 'chapter-1-a').i], [70, 8]);
  const stored = server.rows.get(code).data['chapter-1-a'];
  assert.deepEqual([stored.p, stored.i], [70, 8]);
});

test('a reset made after the progress beats it on every device, and later reading counts again', async () => {
  const server = fakeServer();
  const a = device(server, { 'pos:chapter-1-a': '{"p":60,"d":0,"i":5,"t":1000}' });
  click(a, 'syncStart'); await settle();
  const code = a.storage.sync_code;
  const b = device(server, { sync_code: code, 'pos:chapter-1-a': '{"p":0,"d":0,"i":0,"t":2000,"z":1}' });
  await settle();                                   // the boot sync pushes B's reset
  click(a, 'syncNow'); await settle();
  assert.equal(pos(a, 'chapter-1-a').p, 0);
  assert.equal(pos(a, 'chapter-1-a').z, 1);
  a.storage['pos:chapter-1-a'] = '{"p":15,"d":0,"i":2,"t":3000}';   // A reads again afterwards
  click(a, 'syncNow'); await settle(); click(b, 'syncNow'); await settle();
  assert.equal(pos(b, 'chapter-1-a').p, 15);
});

test('a write that loses a race is merged again and retried', async () => {
  const server = fakeServer({ failPuts: 1 });
  const a = device(server, { 'pos:chapter-1-a': '{"p":30,"d":0,"i":2}' });
  click(a, 'syncStart'); await settle();
  assert.equal(server.calls.filter(c => c.name === 'sync_put').length, 2);
  assert.equal(server.rows.get(a.storage.sync_code).data['chapter-1-a'].p, 30);
  assert.match(a.elements.syncStatus.textContent, /Synced|Синхронізовано/);
});

test('a bad code is refused without touching the network', async () => {
  const server = fakeServer();
  const a = device(server);
  a.elements.syncInput.value = 'not-a-code';
  click(a, 'syncJoin'); await settle();
  assert.equal(server.calls.length, 0);
  assert.equal(a.storage.sync_code, undefined);
});

test('an unreachable server leaves local progress alone and says so', async () => {
  const a = bootReader({ en, uk, sync: CONFIG, storage: { 'pos:chapter-1-a': '{"p":50,"d":0,"i":4}' },
    fetch: async () => { throw new Error('offline'); } });
  click(a, 'syncStart'); await settle();
  assert.equal(pos(a, 'chapter-1-a').p, 50);
  assert.match(a.elements.syncStatus.textContent, /failed|Не вдалося/);
});

test('turning sync off keeps the progress and forgets the code', async () => {
  const server = fakeServer();
  const a = device(server, { 'pos:chapter-1-a': '{"p":50,"d":0,"i":4}' });
  click(a, 'syncStart'); await settle(); click(a, 'syncStop');
  assert.equal(a.storage.sync_code, undefined);
  assert.equal(pos(a, 'chapter-1-a').p, 50);
  assert.equal(a.elements.syncOff.hidden, false);
});

test('the sync strings are localized', () => {
  const a = bootReader({ en, uk, sync: CONFIG, fetch: fakeServer().fetch, browserLanguage: 'uk-UA' });
  assert.equal(a.elements.syncStart.textContent, 'Увімкнути синхронізацію');
  assert.equal(bootReader({ en, uk, sync: CONFIG, fetch: fakeServer().fetch }).elements.syncStart.textContent, 'Turn on sync');
});
