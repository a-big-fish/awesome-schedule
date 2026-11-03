const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

test('cached add schedule page initializes its type switch once after navigation', () => {
  const source = fs.readFileSync('app/static/js/personal_time_picker.js', 'utf8');
  const listeners = {};
  const input = {disabled: false};
  const position = {hidden: false, querySelector: () => input};
  const invitation = {hidden: false};
  const divider = {hidden: false};
  const eventType = {dataset: {}, value: 'personal', changes: [],
    addEventListener(name, callback) {this.changes.push(callback);}};
  const nodes = {'[data-event-type]': eventType, '[data-position-field]': position,
    '[data-invitation-form]': invitation, '[data-invitation-divider]': divider};
  const document = {
    querySelector: selector => nodes[selector] || null,
    querySelectorAll: () => [],
    addEventListener(name, callback) {listeners[name] = callback;},
  };
  vm.runInNewContext(source, {document});
  listeners['personal:navigated']();
  assert.equal(position.hidden, true);
  assert.equal(input.disabled, true);
  assert.equal(invitation.hidden, true);
  eventType.value = 'meeting';
  eventType.changes[0]();
  assert.equal(invitation.hidden, false);
  assert.equal(divider.hidden, false);
  assert.equal(position.hidden, true);
  eventType.value = 'interview';
  eventType.changes[0]();
  assert.equal(position.hidden, false);
  assert.equal(input.disabled, false);
  listeners['personal:navigated']();
  assert.equal(eventType.changes.length, 1);
});


test('weekend start disables and clears skip checkbox, weekday return defaults checked', () => {
  const listeners = {};
  const start = {value: '2026-11-06T08:00', addEventListener(name, fn) {listeners.change = fn;}};
  const hint = {textContent: ''};
  const checkbox = {dataset: {}, checked: true, disabled: false,
    closest() {return {querySelector: selector => selector.includes('start_at') ? start : hint};}};
  const document = {querySelector: () => null, addEventListener(name, fn) {listeners[name] = fn;},
    querySelectorAll: selector => selector === '[data-skip-weekends]' ? [checkbox] : []};
  vm.runInNewContext(fs.readFileSync('app/static/js/personal_time_picker.js', 'utf8'), {document});
  listeners['personal:navigated']();
  assert.equal(checkbox.disabled, false);
  assert.equal(checkbox.checked, true);
  start.value = '2026-11-07T08:00'; listeners.change();
  assert.equal(checkbox.disabled, true);
  assert.equal(checkbox.checked, false);
  assert.ok(hint.textContent);
  start.value = '2026-11-09T08:00'; listeners.change();
  assert.equal(checkbox.disabled, false);
  assert.equal(checkbox.checked, true);
});
