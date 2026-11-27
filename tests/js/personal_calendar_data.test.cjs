const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('app/static/js/personal_calendar_data.js', 'utf8');

function harness() {
  const values = new Map();
  class FormDataStub {
    constructor(form) {this.form = form;}
    get(name) {return values.get(name) || null;}
    getAll(name) {return values.get(name) ? [].concat(values.get(name)) : [];}
  }
  const document = {
    readyState: 'complete',
    querySelector(selector) {
      if (selector === 'meta[name="calendar-user"]') return {content: 'user-1'};
      if (selector === 'meta[name="calendar-csrf"]') return {content: 'csrf'};
      return null;
    },
    querySelectorAll: () => [],
    addEventListener: (name, callback) => {document.listeners[name] = callback;},
    listeners: {},
    createElement: () => ({dataset: {}, style: {}, setAttribute() {}, append() {}, replaceChildren() {}}),
  };
  const context = {
    document, FormData: FormDataStub, Date, URL, AbortController, console,
    location: {pathname: '/my-schedule/events/abc', href: 'https://test.local/my-schedule/events/abc'},
    navigator: {onLine: true},
    window: {awesomeInterviewCalendarModel: {
        wallMs: (day, minutes) => Date.parse(
          `${day}T${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}:00+08:00`),
      },
      addEventListener() {}, removeEventListener() {}, dispatchEvent() {},
      awesomeInterviewCalendarOutbox: {
        SyncEngine: class {constructor() {this.wake = () => {}; this.stop = () => {}; this.flush = async () => {};}},
        rebase: state => state, active: () => true,
      }},
    setTimeout: (fn) => {fn(); return 1;}, clearTimeout() {},
  };
  vm.runInNewContext(source, context);
  return {api: context.window.awesomeInterviewCalendarData, values};
}

function rowHarness({counter = '3'} = {}) {
  const list = {cleared: false, replaceChildren() {this.cleared = true;}};
  const counterNode = {textContent: counter};
  const section = {
    querySelector(selector) {
      if (selector === '.personal-side-heading .muted-text') return counterNode;
      if (selector === '.personal-side-list') return list;
      return null;
    },
  };
  const row = {removed: false, remove() {this.removed = true;}, closest: () => section};
  const form = {action: '', closest: () => row};
  return {form, row, section, list, counterNode};
}

function submit(app, form, payload) {
  app.values.clear();
  for (const [key, value] of Object.entries(payload)) app.values.set(key, value);
  app.api.reflectLocalRow(form);
}

test('a queued completion or defer leaves the sidebar immediately', () => {
  const app = harness();
  const completion = rowHarness();
  completion.form.action = '/my-schedule/events/abc/completion';
  submit(app, completion.form, {completed: 'true'});
  assert.equal(completion.row.removed, true);
  assert.equal(completion.counterNode.textContent, '2');

  const defer = rowHarness({counter: '1'});
  defer.form.action = '/my-schedule/events/abc/overdue';
  submit(app, defer.form, {action: 'defer'});
  assert.equal(defer.row.removed, true);
  assert.equal(defer.counterNode.textContent, '0');
  assert.equal(defer.list.cleared, true);
});

test('restores, edits and grid forms leave the sidebar lists alone', () => {
  const app = harness();
  const restore = rowHarness();
  restore.form.action = '/my-schedule/events/abc/completion';
  submit(app, restore.form, {completed: 'false'});
  assert.equal(restore.row.removed, false);
  assert.equal(restore.counterNode.textContent, '3');

  const edit = rowHarness();
  edit.form.action = '/my-schedule/events/abc/edit';
  submit(app, edit.form, {completed: 'true'});
  assert.equal(edit.row.removed, false);

  const cancel = rowHarness();
  cancel.form.action = '/my-schedule/events/abc/overdue';
  submit(app, cancel.form, {action: 'cancel'});
  assert.equal(cancel.row.removed, true);
});

test('the submitted command uses the picked times, not the cached shell values', () => {
  const app = harness();
  const form = {};
  const payload = {
    title: '去重庆耍', event_type: 'personal', timezone: 'Asia/Shanghai',
    start_at: '2026-11-16T08:00', end_at: '2026-11-16T15:18',
    repeat_enabled: 'false', source: 'manual', raw_text: '',
  };
  app.values.clear();
  for (const [key, value] of Object.entries(payload)) app.values.set(key, value);
  const event = app.api.formValues(form);
  assert.equal(event.start_at, '2026-11-16T00:00:00.000Z');
  assert.equal(event.end_at, '2026-11-16T07:18:00.000Z');
  assert.equal(event.title, '去重庆耍');
  assert.equal(event.repeat_weekdays.length, 0);
});

test('handlesMutation still owns every local-first calendar write', () => {
  const app = harness();
  const form = action => ({method: 'post', action});
  assert.equal(app.api.handlesMutation(form('/my-schedule/events')), true);
  assert.equal(app.api.handlesMutation(form('/my-schedule/events/abc/edit')), true);
  assert.equal(app.api.handlesMutation(form('/my-schedule/events/abc/priority')), true);
  assert.equal(app.api.handlesMutation(form('/my-schedule/events/abc/completion')), true);
  assert.equal(app.api.handlesMutation(form('/my-schedule/events/abc/overdue')), true);
  assert.equal(app.api.handlesMutation(form('/my-schedule/events/abc/close-repeat')), true);
  assert.equal(app.api.handlesMutation({method: 'get', action: '/my-schedule/events/abc/edit'}), false);
  assert.equal(app.api.handlesMutation(form('/my-schedule/ideas')), false);
});
