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
