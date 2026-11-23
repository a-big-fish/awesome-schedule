const {test}=require('node:test');
const assert=require('node:assert/strict');
const model=require('../../app/static/js/personal_calendar_model.js');
const event=(extra={})=>({id:'event',type:'personal',title:'任务',start_at:'2026-11-09T08:00:00+08:00',end_at:'2026-11-16T18:00:00+08:00',timezone:'Asia/Shanghai',repeat_weekdays:[],...extra});
const snapshot=events=>({events,workday_start:'09:30',workday_end:'17:30'});
const now=Date.parse('2026-11-13T12:00:00+08:00');

test('month markup identifies the selected day separately from today', () => {
  const html = model.monthHTML(model.project(snapshot([]), '2026-11-26', 'month', now));
  assert.match(html, /is-selected"><a class="personal-period-date"[^>]*date_value=2026-11-26/);
  assert.match(html, /is-today"><a class="personal-period-date"[^>]*date_value=2026-11-13/);
});

test('local projection clips work hours, skips weekends and keeps midnight end exclusive',()=>{
  const ctx=model.project(snapshot([event({workday_only:true,skip_weekends:true})]),'2026-11-13','week',now);
  const e=ctx.expanded[0],friday=ctx.segment(e,'2026-11-13');
  assert.equal(friday.startMin,570);assert.equal(friday.endMin,1050);
  assert.equal(ctx.segment(e,'2026-11-14'),null);
  const midnight=model.project(snapshot([event({end_at:'2026-11-14T00:00:00+08:00'})]),'2026-11-14','day',now);
  assert.equal(midnight.segment(midnight.expanded[0],'2026-11-14'),null);
  assert.equal(ctx.expanded[0].start_at,'2026-11-09T08:00:00+08:00');
});

test('repeat rule expands only requested dates, records daily completion, respects closure',()=>{
  const repeat=event({end_at:'2026-11-09T10:00:00+08:00',repeat_weekdays:[0,1,2,3,4],repeat_closed_on:'2026-11-14',repeat_completions:{'2026-11-12':'2026-11-12T10:00:00+08:00','2026-11-13':'2026-11-13T10:00:00+08:00'}});
  const ctx=model.project(snapshot([repeat]),'2035-01-01','week',now);
  assert.ok(ctx.expanded.length<15);
  assert.ok(ctx.expanded.every(e=>e.id.split('~')[1]<'2026-11-14'));
  const today=ctx.expanded.find(e=>e.id==='event~2026-11-13');
  assert.ok(today.completed_at);assert.equal(today.repeat_streak,2);assert.equal(today.repeat_missed,3);
  assert.equal(repeat.id,'event');
});

test('repeat wall-clock times follow timezone DST fold and gap, matching ZoneInfo fold=0',()=>{
  assert.equal(new Date(model.wallMs('2026-11-01',90,'America/New_York')).toISOString(),'2026-11-01T05:30:00.000Z');
  assert.equal(new Date(model.wallMs('2026-03-08',150,'America/New_York')).toISOString(),'2026-03-08T07:30:00.000Z');
  const ctx=model.project(snapshot([event({start_at:'2026-11-09T08:00:00-05:00',end_at:'2026-11-09T09:00:00-05:00',timezone:'America/New_York',repeat_weekdays:[0,1,2,3,4]})]),'2026-11-13','day',now);
  const occurrence=ctx.expanded.find(e=>e.id==='event~2026-11-13');
  assert.equal(ctx.segment(occurrence,'2026-11-13').startMin,1260);
});

test('week and month remain Monday-first across year boundaries; titles are escaped',()=>{
  assert.equal(model.datesFor('2027-01-01','week')[0],'2026-12-28');
  assert.equal(model.datesFor('2027-01-01','month').length,42);
  const ctx=model.project(snapshot([event({title:'<img src=x onerror=alert(1)>"'})]),'2026-11-13','day',now);
  assert.ok(!model.dayHTML(ctx).includes('<img'));
  assert.ok(model.dayHTML(ctx).includes('&lt;img'));
  assert.equal(model.scheduledCount('2026-11-09','2026-11-15',[0,1,2,3,4]),5);
});
