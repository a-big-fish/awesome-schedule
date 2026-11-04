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


test('checking skip weekends and submitting each require their own confirmation', () => {
  const listeners = {};
  const start = {value:'2026-11-04T08:00',closest:()=>null};
  const end = {value:'2026-11-07T18:00',closest:()=>null};
  const skip = {name:'skip_weekends',checked:true,disabled:false,closest:()=>form};
  const form = {querySelector(selector) {return selector.includes('start_at') ? start : selector.includes('end_at') ? end : selector==='[data-skip-weekends]' ? skip : null;}};
  let accept=false, confirmations=0;
  const document={addEventListener(name, fn){listeners[name]=fn;}};
  vm.runInNewContext(fs.readFileSync('app/static/js/personal_time_validation.js','utf8'),{document,window:{confirm(){confirmations++;return accept;}}});
  listeners.change({target:skip}); assert.equal(skip.checked,false);
  accept=true;skip.checked=true;listeners.change({target:skip});assert.equal(confirmations,2);
  let blocked=false;
  const submit={target:form,preventDefault(){blocked=true;},stopImmediatePropagation(){}};
  listeners.submit(submit);assert.equal(confirmations,3);assert.equal(blocked,false);
  accept=false;listeners.submit(submit);assert.equal(blocked,true);assert.equal(confirmations,4);
  skip.checked=false;listeners.submit(submit);assert.equal(confirmations,4);
});
