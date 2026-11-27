const {test}=require('node:test'),assert=require('node:assert/strict');
const model=require('../../app/static/js/personal_idea_model.js');
const {SyncEngine}=require('../../app/static/js/personal_calendar_outbox.js');
const initial=()=>({user_id:'u',confirmed_ideas:[],outbox:[]});
const op=(id,action,title='title')=>({id,command:{action,idea_id:'idea',title,note:'note',reference_links:[]},created_at:'2026-11-17T00:00:00Z',state:'pending'});
test('create then edit stays optimistic across creation acknowledgement and reload',()=>{
 const create=op('1','create'),edit=op('2','edit','new'),state=initial();state.outbox=[create,edit];model.rebase(state);
 assert.equal(state.ideas[0].title,'new');
 model.ack(state,create,{idea:{...state.ideas[0],title:'title'}});
 assert.equal(state.ideas[0].title,'new');assert.equal(state.outbox.length,1);
 assert.equal(model.rebase(structuredClone(state)).ideas[0].title,'new');
});
test('permanent rejection rolls back dependents and preserves their drafts',()=>{
 const state=initial(),create=op('1','create');state.outbox=[create,op('2','edit')];model.rebase(state);
 model.fail(state,create,'invalid',422);assert.deepEqual(state.ideas,[]);
 assert.equal(state.outbox.length,2);assert.ok(state.outbox.every(op=>op.state==='failed'));
});
test('offline retry reuses operation id, acknowledges without a GET, and abandonment is immediate',async()=>{
 let state=initial(),attempt=0;const ids=[];const create=op('1','create');state.outbox=[create];model.rebase(state);
 const engine=new SyncEngine({read:async()=>state,update:async change=>{state=change(state);},ack:model.ack,fail:model.fail,
  now:()=>100000,send:async item=>{ids.push(item.id);if(!attempt++)return {status:0};return {status:200,body:{operation_id:item.id,user_id:'u',idea:state.ideas[0]}};}});
 await engine.flush();assert.equal(state.outbox.length,1);state.outbox[0].next_attempt=0;
 await engine.flush();assert.deepEqual(ids,['1','1']);assert.equal(state.outbox.length,0);
 state.outbox.push(op('2','abandon'));model.rebase(state);assert.equal(state.ideas[0].status,'abandoned');engine.stop();
});
