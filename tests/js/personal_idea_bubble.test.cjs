const {test}=require('node:test');
const assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
const source=fs.readFileSync('app/static/js/personal_idea_bubble.js','utf8');
const tick=()=>new Promise(resolve=>setImmediate(resolve));
function browser() {
 const events={};let data=null,calendar={events:[]};
 const element=()=>({children:[],textContent:'',append(...items){this.children.push(...items)},replaceChildren(){this.children=[]},setAttribute(){},addEventListener(){},focus(){this.focused=true}});
 const parts=Object.fromEntries(['[data-idea-count]','[data-idea-preview]','[data-idea-label]','[data-idea-heading]','[data-idea-hint]','[data-idea-cta]','[data-idea-items]','[data-idea-close]','summary'].map(key=>[key,element()]));
 const bubble={...element(),hidden:true,open:false,querySelector:key=>parts[key],contains:()=>false};
 const document={hidden:false,querySelector:()=>bubble,createElement:element,addEventListener:(key,callback)=>events[key]=callback};
 const context={document,location:{pathname:'/my-schedule'},encodeURIComponent,
  window:{awesomeInterviewIdeaData:{ensure:async()=>data},awesomeInterviewCalendarData:{snapshot:()=>calendar},awesomeInterviewCalendarOutbox:{eventId:async key=>key}},
  fetch:()=>{throw Error('the bubble must not fetch its own summary');}};
 vm.runInNewContext(source,context);
 return {events,parts,bubble,context,async update(ideas,scheduled={}){data={user_id:'a',ideas,scheduled};events['personal:ideas-changed']();await tick();},calendar:value=>calendar=value};
}
const idea=(id,title)=>({id,title,status:'pending'});
test('bubble uses the shared local state, renders titles as text and retains the existing design',async()=>{
 const app=browser();await app.update([idea('a','<script>unsafe</script>')]);
 assert.equal(app.bubble.hidden,false);assert.equal(app.parts['[data-idea-label]'].textContent,'待安排');
 assert.equal(app.parts['[data-idea-preview]'].textContent,'<script>unsafe</script>');
 await app.update([]);assert.equal(app.parts['[data-idea-label]'].textContent,'添加idea');assert.equal(app.parts['[data-idea-count]'].hidden,true);
 app.context.location.pathname='/my-schedule/new';app.events['personal:navigated']();assert.equal(app.bubble.hidden,true);
});
test('optimistically arranged and abandoned ideas disappear without a server read',async()=>{
 const app=browser();app.calendar({events:[{id:'idea:a:a'}]});
 await app.update([idea('a','arranged'),{...idea('b','abandoned'),status:'abandoned'},idea('c','visible')]);
 assert.equal(app.parts['[data-idea-count]'].textContent,1);assert.equal(app.parts['[data-idea-preview]'].textContent,'visible');
});
test('escape closes bubble and returns focus to its native disclosure control',async()=>{
 const app=browser();await app.update([idea('id','idea')]);app.bubble.open=true;app.events.keydown({key:'Escape'});
 assert.equal(app.bubble.open,false);assert.equal(app.parts.summary.focused,true);
});
