(() => {
  const scope=document.querySelector('meta[name="calendar-user"]')?.content;
  if(!scope||document.querySelector('meta[name="guest-browsing"]'))return;
  const model=window.awesomeInterviewCalendarModel,TTL=18*60*60*1000;
  if(!model)return;
  let snapshot=null,pending=null,hydratePromise=null,generation=0,stopped=false,controller=null;
  const channel=typeof BroadcastChannel==='function'?new BroadcastChannel('awesome-interview-calendar-data-v1'):null;
  const fresh=entry=>entry?.schema_version===1&&entry.user_id===scope&&Array.isArray(entry.events)&&typeof entry.shell==='string'&&Date.now()-entry.fetched_at>=0&&Date.now()-entry.fetched_at<TTL;
  const csrf=()=>document.querySelector('meta[name="calendar-csrf"]')?.content||'';
  let deviceId;
  try{deviceId=localStorage.getItem('awesome-interview-device-id');if(!deviceId){deviceId=crypto.randomUUID();localStorage.setItem('awesome-interview-device-id',deviceId)}}catch{deviceId='memory-only'}
  function stored(action,value) {
    const version=generation;
    return new Promise((resolve,reject)=>{
      let db,finished=false;
      const done=(error,result)=>{if(finished)return;finished=true;clearTimeout(timer);db?.close();error?reject(error):resolve(result)};
      const timer=setTimeout(()=>done(Error('Browser storage unavailable')),1500);
      let open;try{open=indexedDB.open('awesome-interview-calendar-v1',1)}catch(error){done(error);return}
      open.onupgradeneeded=()=>{if(!open.result.objectStoreNames.contains('snapshots'))open.result.createObjectStore('snapshots',{keyPath:'user_id'})};
      open.onerror=()=>done(open.error);open.onblocked=()=>done(Error('Browser storage blocked'));
      open.onsuccess=()=>{
        db=open.result;if(finished){db.close();return}
        if(action==='put'&&(stopped||version!==generation)){done(null);return}
        const tx=db.transaction('snapshots',action==='get'?'readonly':'readwrite'),store=tx.objectStore('snapshots');
        const request=action==='get'?store.get(scope):action==='put'?store.put(value):store.clear();
        let result;request.onsuccess=()=>{result=request.result};
        tx.oncomplete=()=>done(null,result);tx.onerror=()=>done(tx.error);tx.onabort=()=>done(tx.error||Error('Browser storage transaction aborted'));
      };
    });
  }
  function shellFrom(documentNode) {
    const main=documentNode.querySelector('.page > main');if(!main)throw Error('Calendar workspace unavailable');
    const copy=main.cloneNode(true);copy.querySelectorAll('script').forEach(node=>node.remove());
    copy.querySelectorAll('[name="csrf_token"]').forEach(input=>{input.value='';input.setAttribute('value','')});
    return copy.outerHTML;
  }
  function persist(){return snapshot&&!stopped?stored('put',snapshot).catch(()=>{}):Promise.resolve();}
  function settings(){
    if(!snapshot)return;
    const start=document.querySelector('meta[name="workday-start"]')?.content,end=document.querySelector('meta[name="workday-end"]')?.content;
    if(/^\d{2}:\d{2}$/.test(start||'')&&/^\d{2}:\d{2}$/.test(end||'')&&start<end){snapshot.workday_start=start;snapshot.workday_end=end;}
  }
  async function hydrate() {
    if(hydratePromise)return hydratePromise;
    const version=generation;
    hydratePromise=(async()=>{
      const initial=document.querySelector('[data-calendar-initial]');
      if(initial){try{const data=JSON.parse(initial.textContent);if(data.user_id===scope&&Array.isArray(data.events)){snapshot={...data,shell:shellFrom(document),fetched_at:Date.now(),device_id:deviceId};persist();return}}catch{}}
      try{const saved=await stored('get');if(version===generation&&!stopped&&fresh(saved))snapshot=saved}catch{}
      settings();
    })();return hydratePromise;
  }
  async function ensure(force=false) {
    await hydrate();if(stopped)return null;
    settings();if(!force&&fresh(snapshot))return snapshot;
    if(pending)return pending;
    const version=generation,requestController=new AbortController();controller=requestController;
    const timeout=setTimeout(()=>requestController.abort(),12000);
    const task=(async()=>{
      try{
        const response=await fetch('/my-schedule/calendar-data',{credentials:'same-origin',cache:'no-store',signal:requestController.signal,headers:{Accept:'application/json'}});
        if(response.status===401||response.status===403||new URL(response.url).pathname.startsWith('/auth/')){forget();document.dispatchEvent(new Event('awesome-interview:session-expired'));return null}
        if(!response.ok)return fresh(snapshot)?snapshot:null;
        const data=await response.json();
        if(stopped||version!==generation)return fresh(snapshot)?snapshot:null;
        if(data.schema_version!==1||data.user_id!==scope||!Array.isArray(data.events)||typeof data.shell!=='string')return null;
        const doc=new DOMParser().parseFromString(data.shell,'text/html');
        snapshot={...data,shell:shellFrom(doc),fetched_at:Date.now(),device_id:deviceId};persist();return snapshot;
      }catch{return fresh(snapshot)?snapshot:null}finally{clearTimeout(timeout);if(pending===task)pending=null}
    })();pending=task;return task;
  }
  function forget(broadcast=true) {
    generation++;stopped=true;snapshot=null;controller?.abort();pending=null;
    void stored('clear').catch(()=>{});if(broadcast)channel?.postMessage({type:'logout'});
  }
  async function applyMutation(result,broadcast=true) {
    if(stopped||result?.user_id!==scope||result.schema_version!==1)return false;
    generation++;controller?.abort();pending=null;
    if(!snapshot)await ensure();if(!snapshot||stopped)return false;
    const id=result.deleted_id||result.event?.id;
    if(id){
      const tombstones=snapshot.tombstones||{};
      if(result.deleted_id){snapshot.events=snapshot.events.filter(e=>e.id!==id);tombstones[id]=result.server_time;}
      else if(result.event){
        const old=snapshot.events.find(e=>e.id===id),time=Date.parse(result.event.updated_at)||0;
        if(time>=(Date.parse(tombstones[id])||0)&&time>=(Date.parse(old?.updated_at)||0)){
          snapshot.events=snapshot.events.filter(e=>e.id!==id);snapshot.events.push(result.event);
        }
      }
      snapshot.tombstones=tombstones;
    }
    snapshot.last_modified_at=result.server_time;await persist();
    if(broadcast)channel?.postMessage({type:'mutation',result});
    document.dispatchEvent(new CustomEvent('personal:calendar-mutated'));return true;
  }
  function render(data,target) {
    const view=['day','week','month'].includes(target.searchParams.get('view'))?target.searchParams.get('view'):'day';
    const day=target.searchParams.get('date_value')||model.parts(Date.now()).day;
    const parsedDay=new Date(day+'T00:00:00Z');
    if(!/^\d{4}-\d{2}-\d{2}$/.test(day)||!Number.isFinite(parsedDay.getTime())||parsedDay.toISOString().slice(0,10)!==day)return null;
    const doc=new DOMParser().parseFromString(`<html><head><title>日程表 - Awesome Interview</title><meta name="workday-start" content="${data.workday_start}"><meta name="workday-end" content="${data.workday_end}"></head><body class="page-wide personal-app personal-scroll-page personal-dashboard-page"><div class="page"><nav class="page-nav"><a href="/my-schedule" aria-current="page">日程表</a></nav>${data.shell}</div></body></html>`,'text/html');
    const calendar=doc.querySelector('.personal-calendar');if(!calendar)return null;
    const toolbar=calendar.querySelector('.calendar-workspace-toolbar')?.outerHTML,sub=calendar.querySelector('.calendar-workspace-subtoolbar')?.outerHTML;
    if(!toolbar||!sub)return null;
    const ctx=model.project(data,day,view),more=['1','true'].includes(target.searchParams.get('show_all'));
    calendar.innerHTML=toolbar+sub+(view==='day'?model.dayHTML(ctx):view==='week'?model.weekHTML(ctx):model.monthHTML(ctx));
    calendar.dataset.selectedDate=day;calendar.dataset.selectedView=view;
    const future=ctx.expanded.filter(e=>!e.completed_at&&!e.cancelled_at&&e.end>ctx.now).map(e=>e.end);
    calendar.dataset.nextDeadline=future.length?new Date(Math.min(...future)).toISOString():'';
    const first=day.slice(0,7)+'-01',month=n=>{const d=new Date(first+'T00:00:00Z');d.setUTCMonth(d.getUTCMonth()+n);return d.toISOString().slice(0,10)};
    const previous=view==='day'?model.shift(day,-1):view==='week'?model.shift(day,-7):month(-1);
    const next=view==='day'?model.shift(day,1):view==='week'?model.shift(day,7):month(1);
    const navigate=d=>`/my-schedule?view=${view}&date_value=${d}`;
    const dates=calendar.querySelectorAll('.calendar-date-navigation a');dates[0].href=navigate(previous);dates[1].href=navigate(next);
    calendar.querySelector('.calendar-date-navigation strong').textContent=view==='day'?day.replaceAll('-',' / '):`${day.slice(0,4)} 年 ${day.slice(5,7)} 月`;
    calendar.querySelector('.personal-today-link').href=navigate(ctx.today);
    calendar.querySelectorAll('.personal-view-switch a').forEach(link=>{const v=new URL(link.getAttribute('href'),location.origin).searchParams.get('view');link.href=`/my-schedule?view=${v}&date_value=${day}`;if(v===view)link.setAttribute('aria-current','page');else link.removeAttribute('aria-current')});
    doc.querySelector('.personal-upcoming').innerHTML=model.sideHTML(ctx,csrf(),more);
    return '<!doctype html>'+doc.documentElement.outerHTML;
  }
  async function page(url) {
    const target=new URL(url,location.href);if(target.origin!==location.origin||target.pathname!=='/my-schedule')return null;
    const data=await ensure();return data&&!stopped?render(data,target):null;
  }
  const handlesMutation=form=>form.method?.toUpperCase()==='POST'&&/^\/my-schedule\/events(?:\/[^/]+\/(?:edit|completion|delete|overdue|close-repeat))?$/.test(new URL(form.action,location.href).pathname);
  document.addEventListener('submit',async event=>{
    const form=event.target;
    if(form.matches('form[action="/auth/logout"]')){forget();return}
    if(event.defaultPrevented||!handlesMutation(form)||form.closest('[data-event-dialog]'))return;
    event.preventDefault();event.stopImmediatePropagation();
    if(form.dataset.calendarSaving)return;form.dataset.calendarSaving='1';
    const button=event.submitter||form.querySelector('button[type=submit], button:not([type])');if(button)button.disabled=true;
    let error=form.querySelector('[data-calendar-save-error]');
    if(!error){error=document.createElement('p');error.dataset.calendarSaveError='';error.setAttribute('role','alert');form.append(error)}error.textContent='';
    try{
      const response=await fetch(form.action,{method:'POST',credentials:'same-origin',cache:'no-store',headers:{'X-Calendar-Cache':'1',Accept:'application/json'},body:new FormData(form)});
      if(response.status===401||response.status===403||new URL(response.url).pathname.startsWith('/auth/')){throw Error('登录状态已过期，请重新登录后再操作。')}
      if(response.status===409||response.status===422){
        const type=response.headers.get('content-type')||'';
        if(type.includes('json')){const body=await response.json();throw Error(typeof body.detail==='string'?body.detail:'请检查填写内容')}
        const doc=new DOMParser().parseFromString(await response.text(),'text/html'),main=doc.querySelector('.page > main');
        if(main){document.querySelector('.page > main').replaceWith(main);document.dispatchEvent(new CustomEvent('personal:navigated',{detail:{resetScroll:false}}));return}
        throw Error('请检查填写内容');
      }
      if(!response.ok)throw Error('保存失败，已保留输入内容，请稍后重试。');
      if(!response.headers.get('content-type')?.includes('json')){snapshot=null;generation++;location.assign(response.url);return}
      const result=await response.json();await applyMutation(result);
      form.closest('dialog')?.close();
      if(location.pathname==='/my-schedule')await window.awesomeInterviewCalendarChanged?.();
      else {
        const selected=result.event?model.parts(Date.parse(result.event.start_at)).day:model.parts(Date.now()).day;
        const url=`/my-schedule?view=day&date_value=${selected}`;
        if(!window.awesomeInterviewNavigate?.(url))location.assign(url);
      }
    }catch(reason){error.textContent=reason.message||'网络异常，已保留输入内容。'}
    finally{delete form.dataset.calendarSaving;if(button)button.disabled=false}
  });
  document.addEventListener('awesome-interview:session-expired',()=>forget());
  channel?.addEventListener('message',event=>{if(event.data?.type==='logout')forget(false);else if(event.data?.type==='mutation'&&event.data.result?.user_id===scope)void applyMutation(event.data.result,false)});
  function init() {
    const toolbar=document.querySelector('.calendar-toolbar-end');
    if(toolbar&&!toolbar.querySelector('[data-calendar-sync]')){
      const button=document.createElement('button');button.type='button';button.className='btn secondary';button.dataset.calendarSync='';button.textContent='同步';button.title='从服务器同步最新日程';
      button.onclick=async()=>{button.disabled=true;button.textContent='同步中';try{await ensure(true);await window.awesomeInterviewCalendarChanged?.()}finally{button.disabled=false;button.textContent='同步'}};toolbar.append(button);
    }
    if(location.pathname==='/my-schedule'||location.pathname==='/my-schedule/experiences/plaza')void ensure();
  }
  window.awesomeInterviewCalendarData={page,ensure,applyMutation,handlesMutation,forget};
  document.addEventListener('personal:navigated',init);
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init);else init();
})();
