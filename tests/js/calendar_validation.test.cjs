const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

test('calendar defaults hidden and binds a freshly restored calendar', () => {
  const handlers = {};
  function calendar() {
    const state = new Set();
    const button = {setAttribute() {}, addEventListener(_, fn) {this.click = fn;}};
    return {button, dataset: {}, classList: {toggle(key,on) {on ? state.add(key) : state.delete(key);}, contains: key => state.has(key)},
      querySelector: () => button, querySelectorAll: () => []};
  }
  let current = calendar();
  const document = {readyState: 'complete', querySelector: () => current, addEventListener(name, fn) {handlers[name] = fn;}};
  vm.runInNewContext(fs.readFileSync('app/static/js/personal_calendar_filter.js', 'utf8'), {document});
  assert.ok(current.classList.contains('hide-completed'));
  current.button.click(); assert.equal(current.classList.contains('hide-completed'), false);
  current = calendar(); handlers['personal:navigated']();
  assert.equal(current.classList.contains('hide-completed'), false);
  current.button.click(); assert.ok(current.classList.contains('hide-completed'));
});

test('date completion and submission validate order and same day repeating range', () => {
  const listeners = {};
  const start = {value: '2026-11-04T10:00', closest: () => null};
  const end = {value: '2026-11-04T09:00', closest: () => null};
  const repeat = {checked: false};
  let error;
  const form = {querySelector(selector) {
    return selector.includes('start_at') ? start : selector.includes('end_at') ? end : selector === '[data-repeat-enabled]' ? repeat : error;
  }, append(node) {error = node;}};
  const document = {createElement: () => ({dataset: {}, setAttribute() {}}), addEventListener(name,fn) {listeners[name] = fn;}};
  vm.runInNewContext(fs.readFileSync('app/static/js/personal_time_validation.js','utf8'), {document});
  const change = () => listeners.change({target: {name:'end_at', closest: () => form}});
  change(); assert.equal(error.hidden,false); assert.ok(error.textContent.includes('晚于'));
  end.value = '2026-11-05T11:00'; repeat.checked=true;
  change(); assert.ok(error.textContent.includes('同一天'));
  let prevented=false;
  listeners.submit({target:form, preventDefault(){prevented=true;},stopImmediatePropagation(){}});
  assert.ok(prevented);
  end.value='2026-11-04T11:00';change();assert.ok(error.hidden);
});
