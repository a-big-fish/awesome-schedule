const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('app/static/js/app.js', 'utf8');
const code = source.slice(source.indexOf('  let nowScrollGeneration'), source.indexOf('  const dateScope'));

test('calendar paints the top before smooth centering, replays on navigation and same-page focus', () => {
  const frames = [], calls = [], listeners = {};
  const line = {hidden: false, getBoundingClientRect: () => ({top: 1100, height: 2})};
  const wrap = {isConnected: true, scrollTop: 0, clientHeight: 400,
    querySelector: () => line, getBoundingClientRect: () => ({top: 100}),
    scrollTo(options) {calls.push(options); this.scrollTop = options.top;}};
  vm.runInNewContext(code, {
    document: {querySelector: () => wrap, addEventListener: (name, callback) => listeners[name] = callback},
    window: {matchMedia: () => ({matches: false})}, requestAnimationFrame: callback => frames.push(callback),
    updateNowLines() {}, scheduleNowLineUpdate() {},
  });
  assert.equal(calls[0].top, 0);
  assert.equal(calls.length, 1);
  frames.shift()();
  assert.equal(calls.length, 1);
  frames.shift()();
  assert.equal(calls[1].behavior, 'smooth');
  assert.equal(calls[1].top, 801);
  listeners['personal:focus-now']();
  assert.equal(calls.at(-1).top, 0);
  listeners['personal:navigated']({detail: {resetScroll: false}});
  assert.equal(calls.length, 3); // Background refresh must not restart scrolling.
  listeners['personal:navigated']({detail: {resetScroll: true}});
  assert.equal(calls.length, 4);
});
