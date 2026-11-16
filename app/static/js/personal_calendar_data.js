(() => {
  const scope=document.querySelector('meta[name="calendar-user"]')?.content;
  if(!scope||document.querySelector('meta[name="guest-browsing"]'))return;
  const model=window.awesomeInterviewCalendarModel,outbox=window.awesomeInterviewCalendarOutbox,TTL=18*60*60*1000;
  if(!model||!outbox)return;
  let snapshot=null,pending=null,hydratePromise=null,generation=0,stopped=false,controller=null,localError='';
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
        const request=action==='get'?store.get(scope):action==='put'?store.put(value):store.delete(scope);
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
  // One record contains confirmed data, projected UI data AND the outbox: one atomic commit.
  function updateState(change) {
    if(stopped)return Promise.reject(Error('登录状态已结束'));
    generation++;controller?.abort();pending=null;
    return new Promise((resolve,reject)=>{
      let db,tx,finished=false;
      const finish=error=>{if(finished)return;finished=true;clearTimeout(timer);db?.close();error?reject(error):resolve(snapshot)};
      const timer=setTimeout(()=>{tx?.abort();finish(Error('浏览器存储不可用，未保存本次修改，请检查站点存储权限。'))},3000);
      let open;try{open=indexedDB.open('awesome-interview-calendar-v1',1)}catch(error){finish(error);return}
      open.onupgradeneeded=()=>{if(!open.result.objectStoreNames.contains('snapshots'))open.result.createObjectStore('snapshots',{keyPath:'user_id'})};
      open.onerror=()=>finish(open.error);open.onblocked=()=>finish(Error('浏览器存储被占用，请关闭旧页面后重试。'));
      open.onsuccess=()=>{
        db=open.result;if(finished||stopped){db.close();finish(Error('登录状态已结束'));return}
        tx=db.transaction('snapshots','readwrite');const store=tx.objectStore('snapshots'),get=store.get(scope);let next;
        get.onsuccess=()=>{try{
          const current=structuredClone(get.result||snapshot);
          if(!current)throw Error('请先加载一次日程再保存');
          current.confirmed_events ||= structuredClone(current.events);current.outbox ||= [];
          next=outbox.rebase(change(current));store.put(next);
        }catch(error){tx.abort();finish(error)}};
        tx.oncomplete=()=>{if(!stopped)snapshot=next;finish()};
        tx.onerror=()=>finish(tx.error);tx.onabort=()=>finish(tx.error||Error('本地保存失败，未提交修改。'));
      };
    });
  }
  function settings(){
    if(!snapshot)return;
    const start=document.querySelector('meta[name="workday-start"]')?.content,end=document.querySelector('meta[name="workday-end"]')?.content;
    if(/^\d{2}:\d{2}$/.test(start||'')&&/^\d{2}:\d{2}$/.test(end||'')&&start<end){snapshot.workday_start=start;snapshot.workday_end=end;}
  }
  async function hydrate() {
    if(hydratePromise)return hydratePromise;
    const version=generation;
    hydratePromise=(async()=>{
      let saved;try{saved=await stored('get')}catch{}
      if(stopped||version!==generation)return;
      const initial=document.querySelector('[data-calendar-initial]');
      if(initial){try{const data=JSON.parse(initial.textContent);if(data.user_id===scope&&Array.isArray(data.events)){
        snapshot={...data,shell:shellFrom(document),fetched_at:Date.now(),device_id:deviceId};
        await updateState(old=>({...snapshot,confirmed_events:data.events,outbox:(old.outbox||[]).map(op=>op.state==='auth'?{...op,state:'pending',next_attempt:0}:op),form_shell:old.form_shell}));return;
      }}catch{}}
      if(saved?.user_id===scope&&(fresh(saved)||saved.outbox?.length)){
        snapshot=outbox.rebase(saved);
        if(saved.outbox?.some(op=>op.state==='auth'))await updateState(s=>{s.outbox.forEach(op=>{if(op.state==='auth'){op.state='pending';op.next_attempt=0}});return s});
      }
      settings();
    })();return hydratePromise;
  }
  async function ensure(force=false) {
    await hydrate();if(stopped)return null;
    settings();if(snapshot?.outbox?.length||!force&&fresh(snapshot))return snapshot;
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
        const next={...data,shell:shellFrom(doc),fetched_at:Date.now(),device_id:deviceId};
        snapshot ||= next;
        try{await updateState(old=>({...next,confirmed_events:data.events,outbox:old.outbox||[],form_shell:old.form_shell}))}
        catch{snapshot=outbox.rebase({...next,confirmed_events:data.events,outbox:snapshot.outbox||[]})}
        return snapshot;
      }catch{return fresh(snapshot)?snapshot:null}finally{clearTimeout(timeout);if(pending===task)pending=null}
    })();pending=task;return task;
  }
  function forget(broadcast=true) {
    generation++;stopped=true;snapshot=null;controller?.abort();pending=null;engine.stop();
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
    snapshot.confirmed_events=structuredClone(snapshot.events);
    snapshot.last_modified_at=result.server_time;await updateState(old=>({...snapshot,outbox:old.outbox||[]}));
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
    await hydrate();
    const data=snapshot||await ensure();return data&&!stopped?render(data,target):null;
  }
  const handlesMutation=form=>form.method?.toUpperCase()==='POST'&&/^\/my-schedule\/events(?:\/[^/]+\/(?:edit|completion|delete|overdue|close-repeat))?$/.test(new URL(form.action,location.href).pathname);
  // The sidebar lists are server-rendered, so a queued completion/defer/cancel must also leave the DOM
  // immediately; otherwise the local-first write looks like it did nothing until the next refresh.
  function reflectLocalRow(form) {
    const match=new URL(form.action,location.href).pathname.match(/\/events\/[^/]+\/(completion|delete|overdue|close-repeat)$/);
    if(!match)return;
    const payload=new FormData(form);
    const action=match[1]==='overdue'?(payload.get('action')||'defer'):match[1];
    if(action==='completion'&&payload.get('completed')!=='true')return;  // 恢复完成：留在“今日已完成”
    if(!['completion','delete','defer','cancel'].includes(action))return;
    const row=form.closest('.personal-side-row, .personal-overdue-row');
    if(!row)return;
    const section=row.closest('.personal-overdue-section');
    const counter=section?.querySelector('.personal-side-heading .muted-text');
    row.remove();
    if(counter&&/^\d+$/.test(counter.textContent.trim())){
      const left=Math.max(0,Number(counter.textContent)-1);
      counter.textContent=String(left);
      if(!left)section.querySelector('.personal-side-list')?.replaceChildren();
    }
  }
  async function confirmOverlap(titles) {
    if(!titles.length)return true;
    return new Promise(resolve=>{
      const dialog=document.createElement('dialog');dialog.className='personal-overlap-dialog';
      dialog.setAttribute('aria-label','日程时间有重叠');
      dialog.innerHTML='<h2>日程时间有重叠</h2><p>以下安排与当前日程重叠：</p><ul></ul><p>确认后仍可保存。</p><div class="settings-actions"><button type="button" class="btn secondary">返回调整</button><button type="button" class="btn">仍要保存</button></div>';
      titles.forEach(title=>{const li=document.createElement('li');li.textContent=title;dialog.querySelector('ul').append(li)});
      const done=value=>{dialog.close();dialog.remove();resolve(value)};
      const buttons=dialog.querySelectorAll('button');buttons[0].onclick=()=>done(false);buttons[1].onclick=()=>done(true);
      dialog.addEventListener('cancel',event=>{event.preventDefault();done(false)});
      document.body.append(dialog);dialog.showModal();
    });
  }
  function formValues(form) {
    const values=new FormData(form),get=name=>String(values.get(name)||''),nullable=name=>get(name)||null;
    const zone=get('timezone')||'Asia/Shanghai';
    const instant=value=>{
      if(!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value))throw Error('请选择有效日期和时间');
      return new Date(model.wallMs(value.slice(0,10),Number(value.slice(11,13))*60+Number(value.slice(14,16)),zone)).toISOString();
    };
    const event={title:get('title').trim(),type:get('event_type'),timezone:zone,
      start_at:instant(get('start_at')),end_at:instant(get('end_at')),
      skip_weekends:get('skip_weekends')==='true',workday_only:get('workday_only')==='true',
      repeat_weekdays:get('repeat_enabled')==='true'?values.getAll('repeat_weekdays').map(Number):[],
      reminder_offsets_minutes:values.getAll('reminder_offsets_minutes').map(Number),
      note:nullable('note'),position:nullable('position'),location_text:nullable('location_text'),location_url:nullable('location_url'),
      source:get('source')==='invitation'?'invitation':'manual',raw_text:get('raw_text'),
      platform:nullable('platform'),meeting_url:nullable('meeting_url'),source_idea_id:nullable('source_idea_id'),
      reference_links:values.getAll('reference_url').map((url,i)=>({url:String(url).trim(),title:String(values.getAll('reference_title')[i]||'').trim()})).filter(link=>link.url)};
    if(!event.title||event.title.length>200)throw Error('标题须为 1 至 200 个字符');
    if(event.end_at<=event.start_at)throw Error('结束时间必须晚于开始时间');
    if(get('repeat_enabled')==='true'&&!event.repeat_weekdays.length)throw Error('请选择重复的星期');
    if(event.repeat_weekdays.length){if(get('start_at').slice(0,10)!==get('end_at').slice(0,10))throw Error('重复任务不允许跨天');event.skip_weekends=false}
    return event;
  }
  function cleanEditNavigation(section) {
    // Older cached shells may already contain several generated return-button rows.
    // Strip them both when reading and writing a shell, rather than caching rendered UI recursively.
    section.querySelectorAll('[data-local-edit-return], :scope > .settings-actions, .personal-edit-heading > a').forEach(node=>node.remove());
  }
  function safeFormShell(form) {
    const section=form.closest('.personal-form-page');if(!section)return snapshot.form_shell;
    const copy=section.cloneNode(true);
    cleanEditNavigation(copy);
    copy.querySelectorAll('script,dialog,.parse-tip').forEach(node=>node.remove());
    copy.querySelectorAll('[name=csrf_token]').forEach(node=>node.remove());
    copy.querySelectorAll('input:not([type=checkbox]):not([type=radio]),textarea').forEach(node=>{node.value='';node.setAttribute('value','');if(node.tagName==='TEXTAREA')node.textContent=''});
    copy.querySelectorAll('[data-bound], [data-time-mode]').forEach(node=>{node.removeAttribute('data-bound');node.removeAttribute('data-time-mode')});
    return copy.outerHTML;
  }
  async function queueForm(form) {
    await ensure();if(!snapshot)throw Error('尚未加载日程，联网加载一次后即可本地保存');
    const path=new URL(form.action,location.href).pathname,match=path.match(/\/events\/([^/]+)\/(.+)$/);
    const data=new FormData(form),get=name=>String(data.get(name)||'');
    let id=crypto.randomUUID(),target=match?decodeURIComponent(match[1]):null;
    const [parent,day]=target?.split('~')||[],old=snapshot.events.find(e=>e.id===parent);
    const command={action:match?match[2].replaceAll('-','_'):'create',event_id:parent};
    if(day)command.occurrence_day=day;
    if(command.action==='overdue')command.action=get('action');
    if(['create','edit'].includes(command.action)){
      command.event=formValues(form);
      if(command.action==='create')command.event_id=await outbox.eventId(command.event.source_idea_id?`idea:${scope}:${command.event.source_idea_id}`:`calendar:${scope}:${id}`);
      const recovery=snapshot.outbox?.find(op=>op.state==='failed'&&op.command.event_id===command.event_id&&op.command.action==='create');
      if(recovery){id=recovery.id;command.action='create'}
      const proposed=command.event;
      const conflicts=snapshot.events.filter(e=>e.id!==command.event_id&&!e.cancelled_at&&Date.parse(e.start_at)<Date.parse(proposed.end_at)&&Date.parse(e.end_at)>Date.parse(proposed.start_at)&&
        (!(e.skip_weekends||proposed.skip_weekends)||(()=>{const start=model.parts(Math.max(Date.parse(e.start_at),Date.parse(proposed.start_at))).day,end=Math.min(Date.parse(e.end_at),Date.parse(proposed.end_at));for(let n=0;n<7;n++){const d=model.shift(start,n);if(model.wallMs(d,0)>=end)break;if(model.weekday(d)<5)return true}return false})()));
      if(!await confirmOverlap(conflicts.map(e=>e.title)))return null;
      command.confirm_overlap=conflicts.length>0||get('confirm_overlap')==='true';
    }
    if(command.action==='completion'){
      command.completed=get('completed')==='true';
      if(old?.repeat_weekdays?.length&&!command.occurrence_day)command.occurrence_day=model.parts(Date.now(),old.timezone).day;
    }
    if(command.action==='defer'){
      const value=get('end_at');command.end_at=new Date(model.wallMs(value.slice(0,10),Number(value.slice(11,13))*60+Number(value.slice(14,16)),old?.timezone)).toISOString();
      if(Date.parse(command.end_at)<Date.now()||Date.parse(command.end_at)<Date.parse(old?.end_at))throw Error('新结束时间不可早于原结束时间或当前时间');
    }
    const op={id,command,created_at:new Date().toISOString(),state:'pending',attempts:0,next_attempt:0,
      title:command.event?.title||old?.title||'日程',closed_on:model.shift(model.parts(Date.now(),old?.timezone).day,1)};
    const shell=['create','edit'].includes(command.action)?safeFormShell(form):null;
    await updateState(state=>{
      if(state.outbox.length>=200)throw Error('待同步操作较多，请先联网同步后再继续');
      state.outbox=state.outbox.filter(item=>!(item.state==='failed'&&item.command.event_id===command.event_id));
      state.outbox.push(op);if(shell)state.form_shell=shell;return state;
    });
    localError='';changed();engine.wake();
    return {event:snapshot.events.find(e=>e.id===command.event_id)||old};
  }
  async function showQueued(result) {
    if(location.pathname==='/my-schedule')await window.awesomeInterviewCalendarChanged?.();
    else {
      const selected=result.event?model.parts(Date.parse(result.event.start_at)).day:model.parts(Date.now()).day;
      const url=`/my-schedule?view=day&date_value=${selected}`;
      if(!window.awesomeInterviewNavigate?.(url))location.assign(url);
    }
  }
  async function eventPage(url) {
    const path=new URL(url,location.href).pathname,match=path.match(/^\/my-schedule\/events\/([^/]+)(\/edit)?$/);
    if(!match)return null;
    await ensure();if(!snapshot)return null;
    const target=decodeURIComponent(match[1]),[parent,day]=target.split('~');
    const failed=snapshot.outbox?.find(op=>op.state==='failed'&&op.command.event_id===parent&&op.command.event);
    let e=snapshot.events.find(e=>e.id===parent)||(failed?{...failed.command.event,id:parent}:null);
    if(!e)return null;
    if(match[2]){
      if(!snapshot.form_shell)return null;
      if(failed)e={...e,...failed.command.event};
      const doc=new DOMParser().parseFromString(snapshot.form_shell,'text/html'),section=doc.querySelector('.personal-form-page'),form=section.querySelector('.personal-event-form');
      cleanEditNavigation(section);
      section.querySelectorAll('form:not(.personal-event-form),.personal-type-selection,.section-description').forEach(node=>node.remove());
      section.querySelector('h1').textContent='编辑日程';form.action='/my-schedule/events/'+parent+'/edit';
      const set=(name,value)=>form.querySelectorAll(`[name="${name}"]`).forEach(node=>{
        if(node.type==='checkbox'){node.checked=Array.isArray(value)?value.map(String).includes(node.value):!!value;if(node.checked)node.setAttribute('checked','');else node.removeAttribute('checked')}
        else if(node.tagName==='SELECT'){[...node.options].forEach(option=>{if(option.value===String(value??''))option.setAttribute('selected','');else option.removeAttribute('selected')})}
        else{node.value=value??'';node.setAttribute('value',value??'');if(node.tagName==='TEXTAREA')node.textContent=value??'';
          // Cached shells arrive with the picker markup already applied: repaint its trigger label.
          if(['start_at','end_at'].includes(name))node.dispatchEvent(new Event('change'))}
      });
      if(!form.querySelector('[name=event_type]')){
        const label=document.createElement('label');label.textContent='类型';const select=document.createElement('select');select.className='input';select.name='event_type';select.dataset.eventType='';
        for(const [type,name] of Object.entries({personal:'个人事项',interview:'面试',meeting:'普通会议',exam:'笔试',course:'课程',recruiting:'招聘活动'})){const option=document.createElement('option');option.value=type;option.textContent=name;select.append(option)}label.append(select);form.prepend(label);
      }
      for(const name of ['title','timezone','note','position','location_text','location_url','source','raw_text','platform','meeting_url','source_idea_id'])set(name,e[name]);
      set('event_type',e.type);set('skip_weekends',e.skip_weekends);set('workday_only',e.workday_only);set('repeat_enabled',!!e.repeat_weekdays?.length);set('repeat_weekdays',e.repeat_weekdays||[]);set('reminder_offsets_minutes',e.reminder_offsets_minutes||[]);
      for(const name of ['start_at','end_at']){const p=model.parts(Date.parse(e[name]),e.timezone);set(name,p.day+'T'+String(Math.floor(p.minutes/60)).padStart(2,'0')+':'+String(p.minutes%60).padStart(2,'0'))}
      const rows=section.querySelector('[data-reference-rows]'),template=rows?.querySelector('[data-reference-row]')?.cloneNode(true);
      if(rows&&template){rows.replaceChildren();for(const link of e.reference_links?.length?e.reference_links:[{title:'',url:''}]){const row=template.cloneNode(true);row.querySelector('[name=reference_title]').setAttribute('value',link.title||'');row.querySelector('[name=reference_url]').setAttribute('value',link.url||'');rows.append(row)}}
      let csrfInput=form.querySelector('[name=csrf_token]');if(!csrfInput){csrfInput=document.createElement('input');csrfInput.type='hidden';csrfInput.name='csrf_token';form.append(csrfInput)}csrfInput.setAttribute('value',csrf());
      form.querySelector('[type=submit]').textContent='保存修改';
      section.querySelectorAll('a[href]').forEach(link=>{if(!link.target)link.href='/my-schedule/events/'+parent});
      let heading=section.querySelector('.personal-edit-heading');
      if(!heading){heading=document.createElement('div');heading.className='personal-toolbar personal-edit-heading';const title=section.querySelector('h1');title.before(heading);heading.append(title)}
      const back=document.createElement('a');back.className='btn secondary';back.dataset.localEditReturn='';back.href='/my-schedule/events/'+parent;back.textContent='返回详情';heading.append(back);
      const deletion=document.createElement('form');deletion.className='personal-delete-form';deletion.method='post';deletion.action='/my-schedule/events/'+parent+'/delete';deletion.innerHTML='<button class="btn secondary" type="submit">删除日程</button>';section.append(deletion);
      return section.outerHTML;
    }
    const esc=model.esc,safeLink=value=>{try{const url=new URL(value);return ['https:','http:'].includes(url.protocol)?esc(url.href):''}catch{return ''}};
    let start=e.start_at,end=e.end_at;
    if(day&&e.repeat_weekdays?.length){const a=model.parts(Date.parse(start),e.timezone),b=model.parts(Date.parse(end),e.timezone);start=new Date(model.wallMs(day,a.minutes,e.timezone)).toISOString();end=new Date(model.wallMs(day,b.minutes,e.timezone)).toISOString()}
    const date=value=>{const p=model.parts(Date.parse(value));return p.day+' '+String(Math.floor(p.minutes/60)).padStart(2,'0')+':'+String(p.minutes%60).padStart(2,'0')};
    return `<section class="card personal-form-page"><div class="personal-event-summary"><span class="personal-type-pill">${esc({personal:'个人事项',meeting:'普通会议',interview:'面试',exam:'笔试',course:'课程',recruiting:'招聘活动'}[e.type])}</span><h1>${esc(e.title)}</h1><dl><div><dt>时间</dt><dd>${date(start)} — ${date(end)}</dd></div>`+
      (e.room_number?`<div><dt>会议号</dt><dd>${esc(e.room_number)}</dd></div>`:'')+
      (safeLink(e.meeting_url)?`<div><dt>会议链接</dt><dd><a class="btn" href="${safeLink(e.meeting_url)}" target="_blank" rel="noopener noreferrer">点击入会</a></dd></div>`:'')+
      (e.location_text||safeLink(e.location_url)?`<div><dt>地点</dt><dd>${esc(e.location_text||'')} ${safeLink(e.location_url)?`<a href="${safeLink(e.location_url)}" target="_blank" rel="noopener noreferrer">查看地图</a>`:''}</dd></div>`:'')+
      (e.reference_links?.length?'<div><dt>参考资料</dt><dd><ol class="personal-reference-list">'+e.reference_links.filter(link=>safeLink(link.url)).map((link,i)=>`<li><a href="${safeLink(link.url)}" target="_blank" rel="noopener noreferrer">${esc(link.title||'参考资料 '+(i+1))}</a><span>${esc(new URL(link.url).hostname)} · 新窗口打开</span></li>`).join('')+'</ol></dd></div>':'')+
      `<div><dt>备注</dt><dd class="personal-summary-note">${esc(e.note||'未填写')}</dd></div></dl><div class="settings-actions"><a class="btn" href="/my-schedule/events/${parent}/edit">编辑日程</a><a class="btn secondary" href="/my-schedule">返回日程表</a></div>`+
      (e.repeat_weekdays?.length&&!e.repeat_closed_on?`<form action="/my-schedule/events/${parent}/close-repeat" method="post"><button class="btn secondary" type="submit">关闭该重复任务</button></form>`:'')+'</div></section>';
  }
  function rememberForm(form){if(!form||!snapshot)return;const shell=safeFormShell(form);if(shell)void updateState(s=>{s.form_shell=shell;return s}).catch(()=>{})}
  document.addEventListener('submit',async event=>{
    const form=event.target;
    if(form.matches('form[action="/auth/logout"]')){
      if(snapshot?.outbox?.length&&!window.confirm('仍有未同步的修改或失败草稿。退出会清除本机数据，确认退出吗？')){event.preventDefault();event.stopImmediatePropagation();return}
      forget();return;
    }
    if(event.defaultPrevented||!handlesMutation(form)||form.closest('[data-event-dialog]'))return;
    event.preventDefault();event.stopImmediatePropagation();
    if(form.dataset.calendarSaving)return;form.dataset.calendarSaving='1';
    const button=event.submitter||form.querySelector('button[type=submit], button:not([type])');if(button)button.disabled=true;
    let error=form.querySelector('[data-calendar-save-error]');
    if(!error){error=document.createElement('p');error.dataset.calendarSaveError='';error.setAttribute('role','alert');form.append(error)}error.textContent='';
    try{
      const result=await queueForm(form);if(!result)return;
      reflectLocalRow(form);
      form.closest('dialog')?.close();
      await showQueued(result);
    }catch(reason){localError=reason.name==='QuotaExceededError'?'浏览器存储空间不足，本次修改未保存，请释放空间后重试。':reason.message||'本地保存失败，已保留输入内容。';error.textContent=localError;statusUI()}
    finally{delete form.dataset.calendarSaving;if(button)button.disabled=false}
  });
  function changed(broadcast=true) {
    document.dispatchEvent(new CustomEvent('personal:calendar-mutated'));
    if(broadcast)channel?.postMessage({type:'state',user_id:scope});
    statusUI();
  }
  let repaintTimer;
  function synced() {
    changed();clearTimeout(repaintTimer);
    repaintTimer=setTimeout(()=>{if(!document.querySelector('dialog[open]'))void window.awesomeInterviewCalendarChanged?.()},50);
  }
  const engine=new outbox.SyncEngine({
    read:async()=>{const saved=await stored('get');return saved?.user_id===scope?saved:snapshot},
    update:updateState,notify:synced,online:()=>navigator.onLine!==false,
    lock:fn=>navigator.locks?navigator.locks.request('calendar-sync:'+scope,fn):fn(),
    send:async op=>{
      const abort=new AbortController(),timer=setTimeout(()=>abort.abort(),15000);
      try{
        const response=await fetch('/my-schedule/calendar-sync/'+op.id,{method:'POST',credentials:'same-origin',cache:'no-store',signal:abort.signal,
          headers:{'Content-Type':'application/json',Accept:'application/json','X-CSRF-Token':csrf()},body:JSON.stringify(op.command)});
        if(response.redirected&&new URL(response.url).pathname.startsWith('/auth/'))return {status:401};
        return {status:response.status,body:await response.json().catch(()=>null)};
      }finally{clearTimeout(timer)}
    }
  });
  document.addEventListener('awesome-interview:session-expired',()=>{
    if(!snapshot?.outbox?.length){forget();return}
    void updateState(state=>{state.outbox.filter(outbox.active).forEach(op=>{op.state='auth';op.error='登录已过期，请重新登录后同步。'});return state}).then(()=>changed());
  });
  channel?.addEventListener('message',async event=>{
    if(event.data?.type==='logout')forget(false);
    else if(event.data?.type==='state'&&event.data.user_id===scope&&!stopped){generation++;controller?.abort();pending=null;snapshot=await stored('get');changed(false);engine.wake();if(!document.querySelector('dialog[open]'))void window.awesomeInterviewCalendarChanged?.()}
  });
  window.addEventListener('online',()=>{void updateState(state=>{state.outbox.filter(op=>op.state==='pending').forEach(op=>op.next_attempt=0);return state}).then(()=>engine.wake(0)).catch(()=>{})});
  function statusUI() {
    const toolbar=document.querySelector('.calendar-workspace-subtoolbar');
    if(!toolbar)return;
    let status=document.querySelector('[data-calendar-outbox-status]');
    if(!status){status=document.createElement('div');status.dataset.calendarOutboxStatus='';status.className='calendar-outbox-status';status.setAttribute('role','status');toolbar.after(status)}
    status.replaceChildren();
    const queue=snapshot?.outbox||[];status.hidden=!queue.length&&!localError;
    if(localError){const error=document.createElement('p');error.textContent=localError;error.setAttribute('role','alert');status.append(error)}
    if(!queue.length)return;
    const pending=queue.filter(outbox.active),failed=queue.filter(op=>op.state==='failed');
    const label=document.createElement('span');label.textContent=pending.some(op=>op.state==='auth')?'登录已过期，修改仍保存在本机。':`已保存到本机，${pending.length} 项待同步${navigator.onLine===false?'（离线）':''}。`;status.append(label);
    if(pending.some(op=>op.state==='auth')){const login=document.createElement('a');login.className='btn secondary';login.href='/auth/login';login.textContent='重新登录';status.append(login)}
    for(const op of failed){
      const row=document.createElement('div'),text=document.createElement('span');text.textContent=`${op.title}：${op.error}，已恢复同步前状态。`;row.append(text);
      const button=(label,fn)=>{const b=document.createElement('button');b.className='btn secondary';b.type='button';b.textContent=label;b.onclick=async()=>{try{await fn()}catch(error){localError=error.message||'本地保存失败';statusUI()}};row.append(b)};
      button(op.status===409?'确认重叠并重试':'重试',async()=>{
        if(op.status===409&&!await confirmOverlap([op.title+'（服务器检测到重叠）']))return;
        await updateState(s=>{s.outbox.filter(x=>x.command.event_id===op.command.event_id).forEach(x=>{x.state='pending';x.next_attempt=0;if(op.status===409)x.command.confirm_overlap=true});return s});changed();void window.awesomeInterviewCalendarChanged?.();engine.wake(0);
      });
      if(['create','edit'].includes(op.command.action))button('修改草稿',()=>window.awesomeInterviewOpenEvent?.('/my-schedule/events/'+op.command.event_id+'/edit'));
      button('放弃本次更改',async()=>{await updateState(s=>{s.outbox=s.outbox.filter(x=>x.command.event_id!==op.command.event_id);return s});changed();void window.awesomeInterviewCalendarChanged?.()});status.append(row);
    }
  }
  let initialProjection=false,initialLoadDone=false;
  function init() {
    const toolbar=document.querySelector('.calendar-toolbar-end');
    if(toolbar&&!toolbar.querySelector('[data-calendar-sync]')){
      const button=document.createElement('button');button.type='button';button.className='btn secondary';button.dataset.calendarSync='';button.textContent='同步';button.title='从服务器同步最新日程';
      button.onclick=async()=>{button.disabled=true;button.textContent='同步中';try{if(snapshot?.outbox?.some(op=>op.state==='pending'))await updateState(s=>{s.outbox.filter(op=>op.state==='pending').forEach(op=>op.next_attempt=0);return s});await engine.flush();if(!snapshot?.outbox?.length)await ensure(true);await window.awesomeInterviewCalendarChanged?.()}catch(error){localError=error.message||'同步失败';statusUI()}finally{button.disabled=false;button.textContent='同步'}};toolbar.append(button);
    }
    if(location.pathname==='/my-schedule'||location.pathname==='/my-schedule/experiences/plaza'){
      if(initialLoadDone){statusUI();engine.wake();return}
      initialLoadDone=true;
      void ensure().then(()=>{
      statusUI();engine.wake();
      if(location.pathname==='/my-schedule'&&!initialProjection){initialProjection=true;if(snapshot?.outbox?.length)void window.awesomeInterviewCalendarChanged?.()}
      });
    }
  }
  window.awesomeInterviewCalendarData={page,ensure,applyMutation,handlesMutation,forget,queueForm,showQueued,eventPage,rememberForm,reflectLocalRow};
  document.addEventListener('personal:navigated',init);
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init);else init();
})();
