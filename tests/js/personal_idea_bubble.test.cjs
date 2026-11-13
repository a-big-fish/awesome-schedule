const {test}=require('node:test');
const assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
const source=fs.readFileSync('app/static/js/personal_idea_bubble.js','utf8');
const tick=()=>new Promise(resolve=>setImmediate(resolve));
function browser() {
  const events={}, timers=[], calls=[];
  const element=()=>({children:[],textContent:'',append(...items){this.children.push(...items)},replaceChildren(){this.children=[]},setAttribute(){},addEventListener(){},focus(){this.focused=true}});
  const parts=Object.fromEntries(['[data-idea-count]','[data-idea-preview]','[data-idea-label]','[data-idea-heading]','[data-idea-hint]','[data-idea-cta]','[data-idea-items]','[data-idea-close]','summary'].map(key=>[key,element()]));
  const bubble={...element(),hidden:true,open:false,querySelector:key=>parts[key],contains:()=>false};
  const document={hidden:false,querySelector:()=>bubble,createElement:element,addEventListener:(key,callback)=>events[key]=callback};
  let now=100000;
  const context={document,location:{pathname:'/my-schedule'},Date:{now:()=>now},AbortController,encodeURIComponent,
    setTimeout(callback){timers.push(callback);return timers.length},clearTimeout(){},fetch:()=>new Promise(resolve=>calls.push(resolve))};
  vm.runInNewContext(source,context);
  timers[0]();
  return {events,calls,parts,bubble,context,age:()=>now+=61000,reply:async(data,status=200)=>{calls.at(-1)({ok:status===200,status,redirected:false,json:async()=>data});await tick()}};
}
test('empty summary still shows the add-idea bubble; titles render as text and cache survives date navigation',async()=>{
 const app=browser();await app.reply({total:1,items:[{id:'id',title:'<script>unsafe</script>'}]});
 assert.equal(app.bubble.hidden,false);assert.equal(app.parts['[data-idea-label]'].textContent,'待安排');assert.equal(app.parts['[data-idea-count]'].hidden,false);assert.equal(app.parts['[data-idea-cta]'].textContent,'查看全部待安排');assert.equal(app.parts['[data-idea-preview]'].textContent,'<script>unsafe</script>');
 app.events['personal:navigated']();assert.equal(app.calls.length,1);
 app.context.location.pathname='/my-schedule/new';app.events['personal:navigated']();assert.equal(app.bubble.hidden,true);
 app.context.location.pathname='/my-schedule';app.age();app.events['personal:navigated']();await app.reply({total:0,items:[]});
 assert.equal(app.bubble.hidden,false);assert.equal(app.parts['[data-idea-label]'].textContent,'添加idea');assert.equal(app.parts['[data-idea-count]'].hidden,true);
 assert.equal(app.parts['[data-idea-heading]'].textContent,'还没有待安排的想法');assert.equal(app.parts['[data-idea-hint]'].hidden,true);
 assert.equal(app.parts['[data-idea-cta]'].textContent,'记录 idea');assert.equal(app.parts['[data-idea-preview]'].textContent,'先记下一个想法');assert.equal(app.parts['[data-idea-items]'].children.length,0);
});
test('bubble stays hidden until the first summary arrives, and toggles back for pending ideas',async()=>{
 const app=browser();assert.equal(app.bubble.hidden,true);
 app.age();app.events['personal:navigated']();await app.reply({total:2,items:[{id:'a',title:'甲'},{id:'b',title:'乙'}]});
 assert.equal(app.bubble.hidden,false);assert.equal(app.parts['[data-idea-count]'].textContent,2);assert.equal(app.parts['[data-idea-heading]'].textContent,'还没安排时间的想法');
 assert.equal(app.parts['[data-idea-hint]'].hidden,false);assert.equal(app.parts['[data-idea-cta]'].textContent,'查看全部待安排');assert.equal(app.parts['[data-idea-preview]'].textContent,'甲');assert.equal(app.parts['[data-idea-items]'].children.length,2);
});
test('failed refresh keeps cached preview and backs off instead of reloading',async()=>{
 const app=browser();await app.reply({total:1,items:[{id:'id',title:'cached'}]});app.age();app.events['personal:navigated']();await app.reply({},503);
 assert.equal(app.parts['[data-idea-preview]'].textContent,'cached');app.events['personal:navigated']();assert.equal(app.calls.length,2);
});
test('post invalidation prevents a stale in-flight response from restoring old ideas',async()=>{
 const app=browser();app.events.submit({target:{method:'post'}});await app.reply({total:1,items:[{id:'id',title:'stale'}]});assert.equal(app.bubble.hidden,true);
});
test('escape closes bubble and returns focus to its native disclosure control',async()=>{
 const app=browser();await app.reply({total:1,items:[{id:'id',title:'idea'}]});app.bubble.open=true;app.events.keydown({key:'Escape'});assert.equal(app.bubble.open,false);assert.equal(app.parts.summary.focused,true);
});
