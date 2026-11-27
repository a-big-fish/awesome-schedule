const {test}=require('node:test');
const assert=require('node:assert/strict');
const {rebase,acknowledge,reject,eventId,retryPlan,SyncEngine}=require('../../app/static/js/personal_calendar_outbox.js');
const base=()=>({user_id:'owner',events:[],confirmed_events:[{id:'one',title:'原任务',repeat_weekdays:[],completed_at:null}],outbox:[]});
const operation=(action,extra={})=>({id:'operation',state:'pending',created_at:'2026-11-13T04:00:00Z',command:{event_id:'one',action,...extra}});
const tick=()=>new Promise(resolve=>setImmediate(resolve));

test('optimistic changes render immediately and an earlier acknowledgement preserves later edits',()=>{
  const state=base(),done=operation('completion',{completed:true}),edit={...operation('edit',{event:{title:'后续修改'}}),id:'later'};
  state.outbox=[done,edit];rebase(state);
  assert.equal(state.events[0].title,'后续修改');assert.ok(state.events[0].completed_at);
  assert.equal(state.confirmed_events[0].title,'原任务');
  acknowledge(state,done,{event:{...state.confirmed_events[0],completed_at:done.created_at}});
  assert.equal(state.events[0].title,'后续修改');assert.equal(state.outbox.length,1);
  // A late response from another tab must not overwrite newer confirmed data.
  state.confirmed_events[0].title='新确认';acknowledge(state,done,{event:{id:'one',title:'旧响应'}});
  assert.equal(state.confirmed_events[0].title,'新确认');
});

test('permanent rejection rolls back the event chain, preserving drafts and unrelated work',()=>{
  const state=base(),edit=operation('edit',{event:{title:'失败修改'}}),later={...operation('delete'),id:'later'},other={...operation('create',{event_id:'two',event:{title:'另一任务'}}),id:'other'};
  state.outbox=[edit,later,other];rebase(state);assert.ok(!state.events.some(e=>e.id==='one'));
  reject(state,edit,'参数错误',422);
  assert.equal(state.events.find(e=>e.id==='one').title,'原任务');
  assert.ok(state.events.some(e=>e.id==='two'));assert.equal(state.outbox[0].command.event.title,'失败修改');
  assert.equal(state.outbox[1].state,'failed');assert.equal(state.outbox[2].state,'pending');
});

test('daily completion and restore affect only that repeat occurrence',()=>{
  const state=base();state.confirmed_events[0].repeat_weekdays=[4];state.confirmed_events[0].repeat_completions={'2026-11-06':'earlier'};
  state.outbox=[operation('completion',{occurrence_day:'2026-11-13',completed:true})];rebase(state);
  assert.ok(state.events[0].repeat_completions['2026-11-13']);assert.equal(state.events[0].repeat_completions['2026-11-06'],'earlier');
  state.outbox.push({...operation('completion',{occurrence_day:'2026-11-13',completed:false}),id:'restore'});rebase(state);
  assert.ok(!state.events[0].repeat_completions['2026-11-13']);assert.equal(state.events[0].repeat_completions['2026-11-06'],'earlier');
});

test('UUID generation matches the server namespace UUIDv5',async()=>{
  assert.equal(await eventId('https://example.com'),'4fd35a71-71ef-5a55-a9d9-aa75c889a6d0');
});

test('confirming a rejected overlap sends a new command and keeps the arranged idea event identity', async () => {
  const op = operation('create', {event:{title:'安排草稿', source_idea_id:'idea'}});
  const state = {...base(), confirmed_events:[], outbox:[op]};
  reject(state, op, '时间冲突', 409);
  assert.deepEqual(state.events, []);
  const retry = await retryPlan(op, state.user_id, 'replacement');
  rebase(retry(state));
  assert.equal(state.outbox[0].id, 'replacement');
  assert.equal(state.outbox[0].command.confirm_overlap, true);
  assert.equal(state.events[0].id, 'one');
});

function harness(send){
  let state=base(),updates=0;state.outbox=[operation('completion',{completed:true})];rebase(state);
  const engine=new SyncEngine({read:async()=>structuredClone(state),update:async fn=>{state=fn(structuredClone(state));updates++;return state},send});
  return {engine,get:()=>state,updates:()=>updates};
}

test('a pending server response never delays optimistic local state; queue advances in order',async()=>{
  let finish;const app=harness(op=>new Promise(resolve=>finish=()=>resolve({status:200,body:{operation_id:op.id,user_id:'owner',event:{id:'one',completed_at:'confirmed'}}})));
  const flushing=app.engine.flush();await tick();
  assert.ok(app.get().events[0].completed_at);assert.equal(app.get().outbox.length,1);
  finish();await flushing;assert.equal(app.get().outbox.length,0);assert.equal(app.get().events[0].completed_at,'confirmed');app.engine.stop();
});

test('network failure retains the same ID with backoff, then retry confirms without any pull',async()=>{
  const ids=[];let calls=0;
  const app=harness(async op=>{ids.push(op.id);if(!calls++)throw Error('response lost');return {status:200,body:{operation_id:op.id,user_id:'owner',event:{id:'one',completed_at:'confirmed'}}}});
  await app.engine.flush();assert.equal(app.get().outbox[0].attempts,1);assert.ok(app.get().outbox[0].next_attempt>Date.now());
  assert.ok(app.get().events[0].completed_at);
  app.get().outbox[0].next_attempt=0;await app.engine.flush();app.engine.stop();
  assert.deepEqual(ids,['operation','operation']);assert.equal(app.get().outbox.length,0);
});

test('authorization failure pauses rather than retrying or discarding the durable draft',async()=>{
  let calls=0;const app=harness(async()=>{calls++;return {status:403}});
  await app.engine.flush();await app.engine.flush();app.engine.stop();
  assert.equal(calls,1);assert.equal(app.get().outbox[0].state,'auth');assert.ok(app.get().events[0].completed_at);
});

test('a 422 rolls back, while an invalid successful response remains queued safely',async()=>{
  const app=harness(async()=>({status:422,body:{detail:'不允许修改'}}));
  await app.engine.flush();app.engine.stop();assert.equal(app.get().events[0].completed_at,null);assert.equal(app.get().outbox[0].error,'不允许修改');
  const invalid=harness(async op=>({status:200,body:{operation_id:op.id,user_id:'another-account'}}));
  await invalid.engine.flush();invalid.engine.stop();assert.equal(invalid.get().outbox.length,1);assert.ok(invalid.get().events[0].completed_at);
});
