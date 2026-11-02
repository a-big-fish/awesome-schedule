const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('app/static/js/personal_page_cache.js', 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));

function browser({hidden = true, calendar = null} = {}) {
  const calls = [], rendered = [], events = {}, timers = [], assigned = [], emitted = [];
  let clock = 0;
  const location = new URL('https://test.local/my-schedule/experiences/plaza');
  location.assign = url => assigned.push(url);
  const main = {replaceWith(next) {rendered.push(next.html);}};
  const document = {
    hidden, activeElement: null, title: 'plaza', documentElement: {outerHTML: 'initial-plaza'},
    body: {className: 'personal-app', classList: {add() {}, remove() {}}},
    querySelector(selector) {
      if (selector === '.page > main') return main;
      if (selector === '.personal-calendar[data-selected-date]') return calendar;
      if (selector.includes('.personal-app .page-nav') || selector.startsWith('.page-nav a[href=')) return {};
      return null;
    },
    querySelectorAll: () => [], addEventListener: (name, callback) => {events[name] = callback;},
    dispatchEvent(event) {emitted.push(event.type);},
  };
  class FakeDate extends Date { static now() { return clock; } }
  const context = {document, location, URL, Date: FakeDate,
    window: {addEventListener() {}, scrollTo() {}},
    history: {pushState(_, __, url) {location.href = new URL(url, location).href;}},
    performance: {now: () => 0}, requestAnimationFrame: callback => callback(),
    Event: class {constructor(type) {this.type = type;}},
    DOMParser: class {parseFromString(html) {return {title: html, body: {className: 'personal-app', classList: {contains: () => true}},
      querySelector: selector => selector === '.page > main' ? {html} : null};}},
    setTimeout(callback, delay) {timers.push({callback, delay});},
    fetch(url) {return new Promise(resolve => calls.push({url, resolve, done: false}));},
  };
  vm.runInNewContext(source, context);
  return {calls, rendered, events, document, context, assigned, emitted,
    age: (milliseconds = 31000) => {clock += milliseconds;},
    async navigate(path) {assert.equal(context.window.awesomeInterviewNavigate(path), true); await tick();},
    async reply(call, html = call.url, status = 200) {
      call.done = true;
      call.resolve({ok: status < 400, status, url: 'https://test.local' + call.url,
        headers: {get: () => 'text/html'}, text: async () => html});
      await tick();
    },
    prefetch: () => timers.find(timer => timer.delay === 800).callback(),
    prefetchDays: () => timers.find(timer => timer.delay === 150).callback(),
  };
}

test('profile and interviews use fresh cached pages without duplicate full-page fetches', async () => {
  const app = browser();
  await app.navigate('/my-schedule/profile');
  await app.reply(app.calls[0], 'profile-html');
  await app.navigate('/my-schedule/interviews');
  await app.reply(app.calls[1], 'interviews-html');
  await app.navigate('/my-schedule/profile');
  await app.navigate('/my-schedule/interviews');
  assert.equal(app.calls.length, 2);
  assert.equal(app.rendered.at(-1), 'interviews-html');
});

test('database outage keeps the page and pauses requests rather than reloading', async () => {
  const app = browser();
  await app.navigate('/my-schedule/profile');
  await app.reply(app.calls[0], 'unavailable', 503);
  assert.equal(app.assigned.length, 0);
  assert.ok(app.emitted.includes('personal:unavailable'));
  await app.navigate('/my-schedule/interviews');
  assert.equal(app.calls.length, 1);
  app.age(30001);
  await app.navigate('/my-schedule/profile');
  assert.equal(app.calls.length, 2);
  await app.reply(app.calls[1], 'recovered-profile');
  assert.equal(app.rendered.at(-1), 'recovered-profile');
});

test('expired page displays first but background refresh does not overwrite focused input', async () => {
  const app = browser();
  await app.navigate('/my-schedule/profile'); await app.reply(app.calls[0], 'old-profile');
  await app.navigate('/my-schedule/experiences/plaza');
  app.age();
  await app.navigate('/my-schedule/profile');
  assert.equal(app.rendered.at(-1), 'old-profile');
  app.document.activeElement = {matches: () => true};
  await app.reply(app.calls[1], 'new-profile');
  assert.equal(app.rendered.at(-1), 'old-profile');
});

test('prefetch of expanded navigation remains sequential', async () => {
  const app = browser({hidden: false});
  app.prefetch(); await tick();
  assert.equal(app.calls.length, 1);
  assert.equal(app.calls[0].url, '/my-schedule/new');
  for (let index = 0; index < 5; index++) {
    assert.equal(app.calls.filter(call => !call.done).length, 1);
    await app.reply(app.calls[index]);
  }
  assert.equal(app.calls.length, 5);
  assert.equal(app.calls.filter(call => !call.done).length, 0);
});

test('add schedule uses a prefetched page without waiting for another request', async () => {
  const app = browser({hidden: false});
  app.prefetch(); await tick();
  await app.reply(app.calls[0], 'new-form-html');
  await app.navigate('/my-schedule/new');
  assert.equal(app.rendered.at(-1), 'new-form-html');
  assert.equal(app.calls.filter(call => call.url === '/my-schedule/new').length, 1);
});

test('calendar date navigation reuses an earlier view without another request', async () => {
  const app = browser();
  const first = '/my-schedule?view=day&date_value=2026-10-30';
  const second = '/my-schedule?view=day&date_value=2026-10-31';
  await app.navigate(first); await app.reply(app.calls[0], 'first-day');
  await app.navigate(second); await app.reply(app.calls[1], 'second-day');
  app.age();
  await app.navigate(first);
  assert.equal(app.calls.length, 2);
  assert.equal(app.rendered.at(-1), 'first-day');
});

test('a deadline invalidates calendar freshness before its 18 hour TTL', async () => {
  const app = browser();
  const day = '/my-schedule?view=day&date_value=2026-10-30';
  await app.navigate(day);
  await app.reply(app.calls[0], '<section data-next-deadline="1970-01-01T00:01:00Z">old</section>');
  await app.navigate('/my-schedule/experiences/plaza');
  app.age(61000);
  await app.navigate(day);
  assert.ok(app.rendered.at(-1).includes('old'));
  assert.equal(app.calls.length, 2);
  await app.reply(app.calls[1], 'updated-overdue-list');
  assert.equal(app.rendered.at(-1), 'updated-overdue-list');
});

test('calendar cache is invalidated after a write form', async () => {
  const app = browser();
  const day = '/my-schedule?view=day&date_value=2026-10-30';
  await app.navigate(day); await app.reply(app.calls[0]);
  app.events.submit({target: {method: 'post'}});
  await app.navigate('/my-schedule?view=day&date_value=2026-10-31');
  await app.reply(app.calls[1]);
  await app.navigate(day);
  assert.equal(app.calls.length, 3);
});

test('calendar page older than 18 hours renders immediately and refreshes', async () => {
  const app = browser();
  const day = '/my-schedule?view=day&date_value=2026-10-30';
  await app.navigate(day); await app.reply(app.calls[0], 'old-day');
  await app.navigate('/my-schedule?view=day&date_value=2026-10-31');
  await app.reply(app.calls[1]);
  app.age(18 * 60 * 60 * 1000 + 1);
  await app.navigate(day);
  assert.equal(app.rendered.at(-1), 'old-day');
  assert.equal(app.calls.length, 3);
  await app.reply(app.calls[2], 'new-day');
  assert.equal(app.rendered.at(-1), 'new-day');
});

test('day view warms the previous and next days without rendering them', async () => {
  const app = browser({hidden: false, calendar: {dataset: {selectedView: 'day', selectedDate: '2026-10-30'}}});
  const task = app.prefetchDays();
  await tick();
  assert.equal(app.calls[0].url, '/my-schedule?view=day&date_value=2026-10-29');
  await app.reply(app.calls[0]);
  assert.equal(app.calls[1].url, '/my-schedule?view=day&date_value=2026-10-31');
  await app.reply(app.calls[1]);
  await task;
  assert.equal(app.rendered.length, 0);
});
