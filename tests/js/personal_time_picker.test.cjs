const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('app/static/js/personal_time_picker.js', 'utf8');

function stubElement(label = '') {
  const node = {
    dataset: {}, style: {}, hidden: false, textContent: label, children: [], listeners: {},
    setAttribute(name, value) {(this.attributes ||= {})[name] = value;},
    removeAttribute() {}, remove() {}, focus() {this.focused = true;}, after() {}, before() {},
    append(...items) {node.children.push(...items);}, replaceChildren(...items) {node.children = items;},
    cloneNode() {return node;}, closest() {return null;}, scrollIntoView() {},
    addEventListener(name, callback) {node.listeners[name] = callback;},
    click() {node.listeners.click?.({});},
    classList: {add() {}, remove() {}, toggle() {}},
    querySelector: () => null, querySelectorAll: () => [],
  };
  return node;
}

function dialogHarness() {
  const parts = {};
  for (const selector of ['[data-time-heading]', '[data-time-progress]', '[data-time-options]',
    '[data-time-back]', '[data-time-now]', '[data-time-close]', '[data-time-cancel]']) {
    parts[selector] = stubElement();
  }
  const dialog = {
    dataset: {}, classList: {add() {}, remove() {}, toggle() {}}, listeners: {},
    querySelector: selector => parts[selector] || parts['[data-time-options]'],
    querySelectorAll: () => [], showModal() {}, close() {dialog.closed = true;},
    addEventListener(name, callback) {dialog.listeners[name] = callback;},
    dispatchEvent(event) {dialog.opened = event; dialog.listeners[event.type]?.(event);},
  };
  return {dialog, parts};
}

function fieldHarness({name, value}) {
  const input = {name, value, dataset: {}, min: '', max: '',
    addEventListener(name, callback) {input.listeners = {...input.listeners, [name]: callback};},
    dispatchEvent(event) {input.lastEvent = event;}};
  const trigger = stubElement();
  const span = stubElement(`${name === 'start_at' ? '开始' : '结束'}`);
  const field = {dataset: {}, querySelector: selector => selector === '[data-time-value]' ? input
    : selector === '[data-time-trigger]' ? trigger : span};
  trigger.closest = () => field;
  return {field, input, trigger, span};
}

function boot({fields, dialog, documentOverrides = {}}) {
  const listeners = {};
  const document = {
    querySelector: selector => selector === '[data-time-dialog]' ? dialog : null,
    querySelectorAll: selector => selector === '.personal-time-field' ? fields : [],
    addEventListener(name, callback) {listeners[name] = callback;},
    createElement: () => stubElement(),
    documentElement: {},
    ...documentOverrides,
  };
  vm.runInNewContext(source, {document, Event: class {constructor(type) {this.type = type;}},
    CustomEvent: class {
    constructor(type, init) {this.type = type; this.detail = init?.detail;}
  }});
  return listeners;
}

test('reinitialization binds one delegated click and resolves the replacement dialog', () => {
  const handlers = {};
  let current = dialogHarness().dialog;
  const {field, trigger} = fieldHarness({name: 'start_at', value: '2026-11-20T09:00'});
  boot({fields: [field], dialog: current, documentOverrides: {
    querySelector: selector => selector === '[data-time-dialog]' ? current : null,
    addEventListener(name, callback) {(handlers[name] ||= []).push(callback);},
  }});
  for (let index = 0; index < 5; index++) {
    handlers['personal:navigated'][0]();
    handlers['personal:event-dialog-rendered'][0]();
  }
  const previous = current;
  current = dialogHarness().dialog;
  handlers['personal:navigated'][0]();
  let opens = 0;
  const dispatch = current.dispatchEvent;
  current.dispatchEvent = event => {opens++; dispatch(event);};
  handlers.click.forEach(callback => callback({target: {closest: () => trigger}}));
  assert.equal(handlers.click.length, 1);
  assert.equal(opens, 1);
  assert.equal(current.opened.detail, field);
  assert.equal(previous.opened, undefined);
});

test('cached add schedule page initializes its type switch once after navigation', () => {
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
    createElement: () => stubElement(),
  };
  vm.runInNewContext(source, {document, CustomEvent: class {constructor(type, init) {this.type = type; this.detail = init?.detail;}}});
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

test('a picker injected after load gets its label and opens through delegated clicks', () => {
  const {dialog} = dialogHarness();
  const {field, input, trigger} = fieldHarness({name: 'start_at', value: '2026-11-20T09:00'});
  const listeners = boot({fields: [field], dialog});
  listeners['personal:navigated']();
  assert.equal(trigger.textContent, '2026/11/20 09:00');
  input.value = '2026-11-21T10:30';
  input.listeners.change();
  assert.equal(trigger.textContent, '2026/11/21 10:30');
  listeners.click({target: {closest: () => trigger}});
  assert.equal(dialog.opened.type, 'time:open');
  assert.equal(dialog.opened.detail, field);
  listeners['personal:navigated']();
  assert.equal(trigger.textContent, '2026/11/21 10:30');
});

test('a date-only start commit pulls the end onto the same day instead of throwing', () => {
  const {dialog, parts} = dialogHarness();
  const start = fieldHarness({name: 'start_at', value: '2026-11-18T19:00'});
  const end = fieldHarness({name: 'end_at', value: '2026-11-15T21:00'});
  const form = {querySelector: () => ({closest: () => end.field})};
  start.field.closest = selector => selector === 'form' ? form : null;
  start.field.__timeMode = 'date';
  const listeners = boot({fields: [start.field, end.field], dialog});
  listeners['personal:navigated']();
  // Open the picker for the start field and confirm the pre-selected date.
  dialog.listeners['time:open']({detail: start.field});
  const confirmDate = parts['[data-time-options]'].children.find(node => node.textContent === '确认日期');
  assert.ok(confirmDate, 'the date-only flow must offer a date confirmation');
  confirmDate.click();
  assert.equal(start.input.value, '2026-11-18T08:00');
  assert.equal(end.input.value, '2026-11-18T09:00');
  assert.equal(end.trigger.textContent, '2026/11/18 09:00');
  assert.equal(dialog.closed, true);
});

test('weekend start disables and clears skip checkbox, weekday return defaults checked', () => {
  const listeners = {};
  const startField = {value: '2026-11-06T08:00', addEventListener(name, fn) {listeners.change = fn;}};
  const hint = {textContent: ''};
  const checkbox = {dataset: {}, checked: true, disabled: false,
    closest() {return {querySelector: selector => selector.includes('start_at') ? startField : hint};}};
  const document = {querySelector: () => null, addEventListener(name, fn) {listeners[name] = fn;},
    createElement: () => stubElement(),
    querySelectorAll: selector => selector === '[data-skip-weekends]' ? [checkbox] : []};
  vm.runInNewContext(source, {document, CustomEvent: class {constructor(type, init) {this.type = type; this.detail = init?.detail;}}});
  listeners['personal:navigated']();
  assert.equal(checkbox.disabled, false);
  assert.equal(checkbox.checked, true);
  startField.value = '2026-11-07T08:00'; listeners.change();
  assert.equal(checkbox.disabled, true);
  assert.equal(checkbox.checked, false);
  assert.ok(hint.textContent);
  startField.value = '2026-11-09T08:00'; listeners.change();
  assert.equal(checkbox.disabled, false);
  assert.equal(checkbox.checked, true);
});
