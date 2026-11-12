const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('app/static/js/personal_references.js', 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));

test('title updates use text and stop after six pending responses', async () => {
  const events = {}, timers = new Map(), link = {textContent: '参考资料 1'};
  const list = {isConnected: true, dataset: {referenceTitles: 'owned-event'}, querySelector: () => link};
  let calls = 0, timerId = 0;
  const document = {hidden: false, querySelector: () => list, addEventListener(name, callback) {events[name] = callback;}};
  vm.runInNewContext(source, {document, window: {addEventListener() {}}, AbortController, encodeURIComponent,
    setTimeout(callback) {const id = ++timerId; timers.set(id, callback); return id;},
    clearTimeout(id) {timers.delete(id);},
    async fetch() {calls++; return {ok: true, async json() {return {links: [{index: 0, title: '<title as text>', pending: true}]};}};},
  });
  events.DOMContentLoaded();
  await tick();
  assert.equal(link.textContent, '<title as text>');
  while (timers.size) {
    const [id, callback] = timers.entries().next().value;
    timers.delete(id); callback(); await tick();
  }
  assert.equal(calls, 6);
  list.isConnected = false;
  events['personal:navigated']();
  await tick();
  assert.equal(calls, 6);
});

function formBrowser() {
 const events={}, replies=[];
 const hint={textContent:''}, name={value:'',dispatchEvent(){}}, input={value:'https://example.com/article'};
 const row={isConnected:true,querySelector:s=>s.includes('reference_url')?input:s.includes('reference_title')?name:hint};
 input.matches=()=>true;input.closest=()=>row;
 const document={addEventListener:(n,cb)=>events[n]=cb,querySelector:()=>null};
 vm.runInNewContext(source,{document,window:{addEventListener(){}},URL,AbortSignal,AbortController,encodeURIComponent,Event:class{},setTimeout,clearTimeout,
 fetch:()=>new Promise(resolve=>replies.push(resolve))});
 return {events,hint,name,input,replies,async respond(data,status=200){replies.at(-1)({ok:status===200,json:async()=>data});await tick();}};
}
test('reference editor announces waiting, completion and lookup failure',async()=>{
 const app=formBrowser();app.events.focusout({target:app.input});
 assert.match(app.hint.textContent,/正在尝试解析/);
 await app.respond({title:'Example title'});assert.equal(app.name.value,'Example title');assert.match(app.hint.textContent,/已自动填入/);
 const failed=formBrowser();failed.events.focusout({target:failed.input});await failed.respond({},503);assert.match(failed.hint.textContent,/暂时无法解析/);
 const empty=formBrowser();empty.events.focusout({target:empty.input});await empty.respond({pending:false});assert.match(empty.hint.textContent,/未获取到/);
});
test('manual edits cancel stale title lookups and their status updates',async()=>{
 const app=formBrowser();app.events.focusout({target:app.input});
 app.name.value='用户名称';app.events.input({target:app.input});
 await app.respond({title:'Stale title'});assert.equal(app.name.value,'用户名称');assert.match(app.hint.textContent,/已填写名称/);
});
