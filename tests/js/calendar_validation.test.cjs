const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const {between, makeSlots, nearestSlot} = require('../../app/static/js/personal_calendar_priority_drag.js');

test('overlapping day cards favor priority, keep ties stable and reuse space for non-overlapping cards', () => {
  const source = fs.readFileSync('app/static/js/personal_calendar_filter.js', 'utf8');
  const code = source.slice(source.indexOf('  const dayStart'), source.indexOf('  function packOverlaps'));
  const card = (id, start, duration, priority) => ({
    id, dataset: {priority},
    style: {getPropertyValue: key => key === '--start-min' ? start : duration}
  });
  const cards = [card('normal', 480, 120, '13'), card('high1', 510, 60, '14'),
    card('high2', 510, 60, '14'), card('low', 530, 30, '2'), card('later', 600, 30)];
  const positions = vm.runInNewContext(code + '; Object.fromEntries(packDayCards(cards, () => 100).map(x => [x.card.id, x.left]));', {cards});
  assert.deepEqual({...positions}, {high1: 10, high2: 130, normal: 250, later: 10, low: 370});
});

test('drag previews reachable slots, preserving the original value and allowing ties at the bounds', () => {
  const source = {priority: 7, left: 230, order: 'p'};
  const peers = [{priority: 20, order: 'm3'}, {priority: 13, order: 'm4'}];
  const slots = makeSlots(source, peers, priority => priority === 20 ? 10 : priority >= 13 ? 120 : 230);
  assert.deepEqual(slots, [{left: 10, priority: 20, order: 'p'}, {left: 120, priority: 16, order: 'p'}, source]);
  assert.equal(nearestSlot(slots, 175, 230).priority, 7);
  assert.equal(nearestSlot(slots, 119, 230).priority, 16);
  assert.equal(nearestSlot(slots, 20, 230).priority, 20);
});

test('same-score slots carry a persistent key on both sides of a peer', () => {
  const peer = {priority: 20, order: 'm2'};
  for (const [order, left] of [['m1', 10], ['m3', 130]]) {
    const source = {priority: 20, order, left};
    const slots = makeSlots(source, [peer], (priority, key) => priority === 20 && key < peer.order ? 10 : 130);
    const next = slots.find(slot => slot.left !== left);
    assert.equal(next.priority, 20);
    assert.equal(next.order < peer.order, left === 130);
  }
  assert.ok(between('m1', 'm100001') > 'm1');
  assert.ok(between('m1', 'm100001') < 'm100001');
});

test('dragging beyond the last tied slot lowers the priority even at the same rendered position', () => {
  const peers = [{priority: 20, order: 'm1'}, {priority: 20, order: 'm2'}];
  const place = (priority, key) => priority < 20 ? 250 : key < 'm1' ? 10 : key < 'm2' ? 130 : 250;
  for (const [left, order] of [[10, 'h'], [250, 's']]) {
    const slots = makeSlots({priority: 20, order, left, width: 100}, peers, place);
    assert.equal(nearestSlot(slots, 250, left).priority, 20);
    const lower = nearestSlot(slots, 310, left);
    assert.equal(lower.priority, 19);
    assert.equal(lower.left, 250);
  }
  const lowest = makeSlots({priority: 1, order: 's', left: 130}, [{priority: 1, order: 'm'}],
    (_, key) => key < 'm' ? 10 : 130);
  assert.equal(nearestSlot(lowest, 260, 130).priority, 1);
});

test('calendar defaults hidden and binds a freshly restored calendar', () => {
  const handlers = {};
  function calendar() {
    const state = new Set();
    const button = {setAttribute() {}, addEventListener(_, fn) {this.click = fn;}};
    return {button, dataset: {}, classList: {toggle(key,on) {on ? state.add(key) : state.delete(key);}, contains: key => state.has(key)},
      querySelector: (selector) => selector === '[data-calendar-toggle]' ? button : null, querySelectorAll: () => []};
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


test('long day cards keep text visible and clamp it inside the event', () => {
  const source = fs.readFileSync('app/static/js/personal_calendar_filter.js', 'utf8');
  const code = source.slice(source.indexOf('  function keepDayTitlesVisible'), source.indexOf('  function init()'));
  let top = -700, offset;
  const frames = [], listeners = {};
  const content = {offsetHeight: 60, style: {setProperty(_, value) {offset = value;}}};
  const card = {clientHeight: 1000, querySelector: () => content, getClientRects: () => [1], getBoundingClientRect: () => ({top})};
  const wrap = {isConnected: true, addEventListener(name, fn) {listeners[name] = fn;}, getBoundingClientRect: () => ({top: 100}), querySelectorAll: () => [card]};
  const calendar = {querySelector: () => wrap, addEventListener() {}};
  vm.runInNewContext(code + '; keepDayTitlesVisible(calendar);', {calendar, requestAnimationFrame(fn) {frames.push(fn);}});
  frames.shift()(); assert.equal(offset, '810px');
  top = -1200; listeners.scroll(); listeners.scroll(); assert.equal(frames.length, 1);
  frames.shift()(); assert.equal(offset, '920px');
  top = 200; listeners.scroll(); frames.shift()(); assert.equal(offset, '0px');
});
