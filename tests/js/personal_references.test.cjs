const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('app/static/js/personal_references.js', 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));

test('title updates use text and stop after six pending responses', async () => {
  const events = {}, timers = new Map(), link = {textContent: '参考资料 1'};
  const list = {isConnected: true, dataset: {referenceTitles: 'owned-event'}, querySelector: () => link};
  let calls = 0, timerId = 0;
  const document = {hidden: false, querySelector: () => list, addEventListener(name, callback) {events[name] = callback;}};
  vm.runInNewContext(source, {document, window: {addEventListener() {}}, AbortController, encodeURIComponent,
    setTimeout(callback) {const id = ++timerId; timers.set(id, callback); return id;},
    clearTimeout(id) {timers.delete(id);},
    async fetch() {calls++; return {ok: true, async json() {return {links: [{index: 0, title: '<title as text>', pending: true}]};}};},
  });
  events.DOMContentLoaded();
  await tick();
  assert.equal(link.textContent, '<title as text>');
  while (timers.size) {
    const [id, callback] = timers.entries().next().value;
    timers.delete(id); callback(); await tick();
  }
  assert.equal(calls, 6);
  list.isConnected = false;
  events['personal:navigated']();
  await tick();
  assert.equal(calls, 6);
});
