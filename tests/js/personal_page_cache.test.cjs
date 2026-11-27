const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('app/static/js/personal_page_cache.js', 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));

test('persisted experience list appears before its background GET and preserves new form input', async () => {
  const app=browser();
  app.context.window.awesomeInterviewPrivatePages={get:async()=>({html:'durable-list',time:Date.now()}),put:async()=>{}};
  await app.navigate('/my-schedule/experiences?q=cache&visibility=private');
  assert.equal(app.rendered.at(-1),'durable-list');
  assert.equal(app.calls.length,1);
  app.events.input({target:{closest:()=>true}});
  await app.reply(app.calls[0],'background-list');
  assert.equal(app.rendered.at(-1),'durable-list');
});

test('failed background list refresh keeps a durable snapshot visible without navigation',async()=>{
  const app=browser();
  app.context.window.awesomeInterviewPrivatePages={get:async()=>({html:'offline-list',time:0})};
  await app.navigate('/my-schedule/experiences');
  assert.equal(app.rendered.at(-1),'offline-list');
  await app.reply(app.calls[0],'unavailable',503);
  assert.equal(app.rendered.at(-1),'offline-list');assert.equal(app.assigned.length,0);
});

test('deadline refresh fetches once without first rendering stale cache or navigating', async () => {
  const app = browser({hidden:false, scrolling:true, initialPath:'/my-schedule?view=day&date_value=2026-11-06'});
  const before = app.context.location.href;
  const task = app.context.window.awesomeInterviewRefreshCalendar();
  assert.equal(app.calls.length, 1);
  assert.equal(app.rendered.length, 0);
  await app.reply(app.calls[0], 'fresh-deadline-calendar');
  await task;
  assert.equal(app.rendered.length, 1);
  assert.equal(app.context.location.href, before);
  assert.equal(app.assigned.length, 0);
  assert.equal(app.restoredScroll[0].scrollTop, 720);
  assert.equal(app.restoredScroll[0].scrollLeft, 35);
});

test('hidden calendar never makes a deadline request', async () => {
  const app = browser({hidden:true, initialPath:'/my-schedule'});
  await app.context.window.awesomeInterviewRefreshCalendar();
  assert.equal(app.calls.length, 0);
  assert.equal(app.rendered.length, 0);
});

function browser({hidden = true, scrolling = false, calendar = null, initialPath = '/my-schedule/experiences/plaza'} = {}) {
  const calls = [], rendered = [], events = {}, windowEvents = {}, timers = [], assigned = [], emitted = [];
  let clock = 0;
  const location = new URL('https://test.local' + initialPath);
  location.assign = url => assigned.push(url);
  const restoredScroll = scrolling ? [{scrollTop:0, scrollLeft:0}] : [];
  const main = {querySelectorAll: () => scrolling ? [{scrollTop:720, scrollLeft:35}] : [], replaceWith(next) {rendered.push(next.html);}};
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
  const context = {document, location, URL, URLSearchParams, Date: FakeDate,
    window: {addEventListener(name, callback) {windowEvents[name] = callback;}, scrollTo() {}},
    history: {pushState(_, __, url) {location.href = new URL(url, location).href;}},
    performance: {now: () => 0}, requestAnimationFrame: callback => callback(),
    Event: class {constructor(type) {this.type = type;}},
    CustomEvent: class {constructor(type, options) {this.type = type; this.detail = options.detail;}},
    DOMParser: class {parseFromString(html) {return {title: html, body: {className: 'personal-app', classList: {contains: () => true}},
      querySelector: selector => selector === '.page > main' ? {html, querySelectorAll: () => restoredScroll} : null};}},
    setTimeout(callback, delay) {timers.push({callback, delay});},
    fetch(url) {return new Promise(resolve => calls.push({url, resolve, done: false}));},
  };
  vm.runInNewContext(source, context);
  return {calls, rendered, events, windowEvents, document, context, assigned, emitted, restoredScroll,
    age: (milliseconds = 301000) => {clock += milliseconds;},
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

test('initial prefetch only warms add form and calendar sequentially', async () => {
  const app = browser({hidden: false});
  app.prefetch(); await tick();
  assert.equal(app.calls.length, 1);
  assert.equal(app.calls[0].url, '/my-schedule/new');
  for (let index = 0; index < 2; index++) {
    assert.equal(app.calls.filter(call => !call.done).length, 1);
    await app.reply(app.calls[index]);
  }
  assert.equal(app.calls.length, 2);
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


test('equivalent calendar query forms and completion filters reuse one snapshot', async () => {
  const app = browser();
  await app.navigate('/my-schedule?view=day&date_value=2026-11-03');
  await app.reply(app.calls[0], 'same-day');
  await app.navigate('/my-schedule?date_value=2026-11-03&show_completed=false');
  assert.equal(app.calls.length, 1);
  assert.equal(app.rendered.at(-1), 'same-day');
});

test('prefetched neighbor is reused when clicked and on repeated prefetch', async () => {
  const app = browser({hidden: false, calendar: {dataset: {selectedView: 'day', selectedDate: '2026-10-30'}}});
  const task = app.prefetchDays(); await tick();
  await app.reply(app.calls[0], 'previous-day');
  await app.reply(app.calls[1], 'next-day'); await task;
  await app.navigate('/my-schedule?date_value=2026-10-31&view=day');
  assert.equal(app.rendered.at(-1), 'next-day');
  assert.equal(app.calls.length, 2);
});


test('show more remains a distinct snapshot and accepts the server numeric flag', async () => {
  const app = browser();
  await app.navigate('/my-schedule?view=day&date_value=2026-11-03'); await app.reply(app.calls[0], 'today-only');
  await app.navigate('/my-schedule?view=day&date_value=2026-11-03&show_all=1');
  assert.ok(app.calls[1].url.endsWith('show_all=true'));
  await app.reply(app.calls[1], 'all-upcoming');
  assert.equal(app.rendered.at(-1), 'all-upcoming');
});


test('initial default calendar URL shares the explicit today cache key', async () => {
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en-CA', {timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit'}).formatToParts(new Date()).map(part => [part.type, part.value]));
  const app = browser({initialPath: '/my-schedule'});
  await app.navigate(`/my-schedule?view=day&date_value=${parts.year}-${parts.month}-${parts.day}`);
  assert.equal(app.calls.length, 0);
});


test('detail and edit navigation keep the calendar cache alive', async () => {
  const app = browser();
  const day='/my-schedule?view=day&date_value=2026-11-04';
  await app.navigate(day);await app.reply(app.calls[0],'calendar');
  await app.navigate('/my-schedule/events/example');await app.reply(app.calls[1],'detail');
  await app.navigate('/my-schedule/events/example/edit');await app.reply(app.calls[2],'edit');
  await app.navigate(day);
  assert.equal(app.calls.length,3);assert.equal(app.rendered.at(-1),'calendar');
});

test('navigation prefetch runs once instead of rescanning expired modules', async () => {
  const app=browser({hidden:false});
  const pending=app.prefetch();await tick();
  for(let index=0;index<2;index++) await app.reply(app.calls[index]);
  await pending;app.age(600000);
  await app.prefetch();assert.equal(app.calls.length,2);
});

test('active navigation link is prevented without another request', () => {
  const app=browser();let prevented=false;
  app.events.click({target:{closest:()=>({href:app.context.location.href})},preventDefault(){prevented=true;}});
  assert.ok(prevented);assert.equal(app.calls.length,0);
});

test('resuming after a minute keeps the snapshot until one background request completes', async () => {
  const app = browser({hidden:false, scrolling:true, initialPath:'/my-schedule'});
  app.document.hidden = true; app.events.visibilitychange(); app.age(61000);
  app.document.hidden = false; app.events.visibilitychange();
  assert.equal(app.calls.length, 1);
  assert.equal(app.rendered.length, 0);
  app.windowEvents.pageshow({persisted:true});
  assert.equal(app.calls.length, 1);
  await app.reply(app.calls[0], 'updated-calendar');
  assert.deepEqual(app.rendered, ['updated-calendar']);
  assert.equal(app.restoredScroll[0].scrollTop, 720);
  assert.equal(app.assigned.length, 0);
});

test('brief tab switches do not send resume requests', () => {
  const app = browser({hidden:false});
  app.document.hidden = true; app.events.visibilitychange(); app.age(10000);
  app.document.hidden = false; app.events.visibilitychange();
  assert.equal(app.calls.length, 0);
});

test('failed resume refresh leaves visible content and never reloads', async () => {
  const app = browser({hidden:false});
  app.windowEvents.pageshow({persisted:true});
  await app.reply(app.calls[0], 'unavailable', 503);
  assert.equal(app.rendered.length, 0);
  assert.equal(app.assigned.length, 0);
});

test('resume refresh cannot overwrite edits made while its request is in flight', async () => {
  const app = browser({hidden:false});
  app.windowEvents.pageshow({persisted:true});
  app.events.input({target:{closest:()=>({})}});
  await app.reply(app.calls[0], 'new-data');
  assert.equal(app.rendered.length, 0);
});

test('resume refresh never replaces the live add form even if it has no input events', async () => {
  const app = browser({hidden:false, initialPath:'/my-schedule/new'});
  app.windowEvents.pageshow({persisted:true});
  assert.equal(app.calls.length, 1);
  await app.reply(app.calls[0], 'updated-add-form');
  assert.equal(app.rendered.length, 0);
});

test('write invalidation discards the result of an older resume refresh', async () => {
  const app = browser({hidden:false});
  app.windowEvents.pageshow({persisted:true});
  app.events.submit({target:{method:'post'}});
  await app.reply(app.calls[0], 'before-save-data');
  assert.equal(app.rendered.length, 0);
});

test('pending resume refresh cannot replace another page after navigation', async () => {
  const app = browser({hidden:false});
  app.windowEvents.pageshow({persisted:true});
  await app.navigate('/my-schedule/interviews');
  await app.reply(app.calls[1], 'interviews');
  await app.reply(app.calls[0], 'old-plaza-result');
  assert.deepEqual(app.rendered, ['interviews']);
});
