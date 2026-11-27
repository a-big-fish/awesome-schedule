const {test}=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
const queue=require('../../app/static/js/personal_calendar_outbox.js'),model=require('../../app/static/js/personal_idea_model.js');
const source=fs.readFileSync('app/static/js/personal_idea_data.js','utf8');
const tick=()=>new Promise(resolve=>setImmediate(resolve));

test('calendar dependency distinguishes a rejected idea from one waiting to sync', async () => {
  const base = {schema_version:1, user_id:'u', confirmed_ideas:[], ideas:[], shell:'synthetic'};
  const commands = [
    {id:'one', state:'failed', error:'标题校验失败', command:{idea_id:'rejected', action:'create'}},
    {id:'two', state:'pending', command:{idea_id:'waiting', action:'create'}},
  ];
  const app = browser({records:new Map([['u', {...base, outbox:commands}]])});
  await app.ready();
  const rejected = await app.api.flushForIdea('rejected');
  assert.equal(rejected.status, 422);
  assert.match(rejected.body.detail, /标题校验失败/);
  const waiting = await app.api.flushForIdea('waiting');
  assert.equal(waiting.status, 0);
  assert.match(waiting.waiting, /等待关联事项同步/);
  assert.equal(await app.api.flushForIdea('accepted'), null);
});
function browser({records=new Map([['u',{schema_version:1,user_id:'u',confirmed_ideas:[],outbox:[],ideas:[],shell:'synthetic'}]]),owner='u'}={}){
 const events={},timers=new Map();let timerId=0,broken=false,fetches=0;
 const indexedDB={open(){if(broken)throw Error('storage disabled');const request={};queueMicrotask(()=>{
  request.result={close(){},transaction(_,mode){
   const tx={},copy=structuredClone(records);let aborted=false;
   tx.abort=()=>{aborted=true;tx.onabort?.();};
   tx.objectStore=()=>({get(key){const result={};queueMicrotask(()=>{
    result.result=copy.get(key);result.onsuccess?.();
    if(!aborted){if(mode==='readwrite'){records.clear();for(const [k,v] of copy)records.set(k,v);}tx.oncomplete?.();}
   });return result;},put(value){copy.set(value.user_id,structuredClone(value));},delete(key){copy.delete(key);}});
   return tx;
  }};request.onsuccess();
 });return request;}};
 const document={hidden:true,querySelector:selector=>selector==='meta[name="calendar-user"]'?{content:owner}:null,
  addEventListener:(type,fn)=>(events[type]||=[]).push(fn),dispatchEvent(){}};
 const context={window:{awesomeInterviewIdeaModel:model,awesomeInterviewCalendarOutbox:queue,addEventListener(){}},document,
  indexedDB,structuredClone,crypto:globalThis.crypto,navigator:{onLine:false},URL,Date,
  location:{pathname:'/my-schedule/experiences/plaza',href:'https://test.local/my-schedule/experiences/plaza'},
  Event:class{constructor(type){this.type=type;}},
  setTimeout(fn){timers.set(++timerId,fn);return timerId;},clearTimeout(id){timers.delete(id);},
  fetch(){fetches++;throw Error('offline');}};
 vm.runInNewContext(source,context);
 return {api:context.window.awesomeInterviewIdeaData,records,events,broken:()=>broken=true,
  async ready(){await tick();await tick();},get fetches(){return fetches;}};
}
test('durable local create/edit restores the projected state and command IDs without network',async()=>{
 const app=browser();await app.ready();
 await app.api.enqueue({action:'create',title:'synthetic',note:'',reference_links:[]},'operation');
 const id=app.api.snapshot().ideas[0].id;
 await app.api.enqueue({action:'edit',idea_id:id,title:'edited',note:'',reference_links:[]},'edit');
 assert.equal(app.records.get('u').outbox.length,2);assert.equal(app.records.get('u').ideas[0].title,'edited');
 const reloaded=browser({records:app.records});await reloaded.ready();
 assert.equal(reloaded.api.snapshot().ideas[0].title,'edited');
 assert.deepEqual(reloaded.api.snapshot().outbox.map(op=>op.id),['operation','edit']);
 assert.equal(app.fetches+reloaded.fetches,0);
});
test('failed IndexedDB commit leaves the visible state unchanged and never sends a write',async()=>{
 const app=browser();await app.ready();app.broken();
 await assert.rejects(app.api.enqueue({action:'create',title:'not saved',note:'',reference_links:[]},'op'),/storage disabled/);
 assert.equal(app.api.snapshot().ideas.length,0);assert.equal(app.records.get('u').outbox.length,0);assert.equal(app.fetches,0);
});
test('another account cannot hydrate the previous account snapshot; logout deletes only its own record',async()=>{
 const first=browser();await first.ready();
 const other=browser({owner:'v',records:first.records});await other.ready();assert.equal(other.api.snapshot(),null);
 for(const fn of first.events.submit)fn({target:{action:'https://test.local/auth/logout',method:'POST'}});
 await tick();await tick();assert.equal(first.records.has('u'),false);
});
