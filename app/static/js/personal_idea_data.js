(() => {
  const scope = document.querySelector('meta[name="calendar-user"]')?.content;
  const model = window.awesomeInterviewIdeaModel, queue = window.awesomeInterviewCalendarOutbox;
  const database = window.awesomeInterviewDatabaseStatus;
  if (!scope || !model || !queue || document.querySelector('meta[name="guest-browsing"]')) return;
  let state = null, hydrated, pending, stopped = false, revision = 0, lastPull = 0, localError = '';
  const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('awesome-interview-ideas') : null;
  const csrf = () => document.querySelector('meta[name="calendar-csrf"]')?.content || '';
  function transaction(change, remove = false) {
    return new Promise((resolve, reject) => {
      let db, tx, done = false;
      const finish = (error, result) => {
        if (done) return; done = true; clearTimeout(timer); db?.close();
        error ? reject(error) : resolve(result);
      };
      const timer = setTimeout(() => {tx?.abort(); finish(Error('浏览器存储不可用，请检查站点存储权限。'));}, 3000);
      let request;
      try {request = indexedDB.open('awesome-interview-ideas-v1', 1);} catch (error) {finish(error); return;}
      request.onupgradeneeded = () => request.result.createObjectStore('states', {keyPath:'user_id'});
      request.onerror = () => finish(request.error);
      request.onblocked = () => finish(Error('浏览器存储被占用，请关闭旧页面后重试。'));
      request.onsuccess = () => {
        db = request.result; if (done) {db.close(); return;}
        if (stopped && !remove) {finish(Error('登录状态已结束')); return;}
        tx = db.transaction('states', change || remove ? 'readwrite' : 'readonly');
        const store = tx.objectStore('states'), get = store.get(scope); let next;
        get.onsuccess = () => {
          try {
            next = get.result;
            if (remove) store.delete(scope);
            else if (change) {
              next = model.rebase(change(structuredClone(next || state)));
              delete next.shell;
              delete next.arrange_shell;
              store.put(next);
            }
          } catch (error) {tx.abort(); finish(error);}
        };
        tx.oncomplete = () => {
          if (change && !stopped) {state = next; revision++;}
          finish(null, next);
        };
        tx.onerror = tx.onabort = () => finish(tx.error || Error('本地保存失败，未提交本次修改。'));
      };
    });
  }
  function hydrate() {
    return hydrated ||= transaction().then(async value => {
      if (stopped || value?.user_id !== scope || value.schema_version !== 1) return;
      state = model.rebase(value);
      if (value.shell || value.arrange_shell || state.outbox.some(op => op.state === 'auth')) {
        await transaction(current => {
          for (const op of current.outbox.filter(op => op.state === 'auth')) {
            Object.assign(op, {state: 'pending', next_attempt: 0});
          }
          return current;
        });
      }
    }).catch(() => {});
  }
  async function ensure() {await hydrate(); if (!state && !stopped) await refresh(); return state;}
  async function refresh() {
    await hydrate();
    if (stopped || document.hidden || state?.outbox?.length || Date.now() - lastPull < 60000) return state;
    if (pending) return pending;
    lastPull = Date.now(); const version = revision;
    pending = (async () => {
      const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 12000);
      try {
        const response = await fetch('/my-schedule/ideas-data?format=json', {credentials:'same-origin', cache:'no-store',
          headers:{Accept:'application/json'}, signal:controller.signal});
        if ([401,403].includes(response.status) || response.redirected) {
          document.dispatchEvent(new Event('awesome-interview:session-expired')); return state;
        }
        if (!response.ok) {database?.report(response, await response.json().catch(()=>null)); return state;}
        const data = await response.json();
        database?.report(response, data);
        if (stopped || revision !== version || data.user_id !== scope || data.schema_version !== 1 ||
            !Array.isArray(data.ideas)) return state;
        const {shell, arrange_shell, ...resource} = data;
        await transaction(old => old?.outbox?.length ? old : {...resource,
          confirmed_ideas:data.ideas, outbox:[], fetched_at:Date.now()});
        changed(false);
      } catch { /* Keep durable local data on network failure. */ }
      finally {clearTimeout(timeout); pending = null;}
      return state;
    })();
    return pending;
  }
  const engine = new queue.SyncEngine({read:() => transaction(), update:transaction,
    ack:model.ack, fail:model.fail, online:() => navigator.onLine !== false,
    lock:fn => navigator.locks ? navigator.locks.request('idea-sync:' + scope, fn) : fn(),
    notify:() => changed(),
    send:async op => {
      const session = await database?.session();
      if (session?.status) return session;
      const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 15000);
      try {
        const response = await fetch('/my-schedule/idea-sync/' + op.id, {method:'POST', credentials:'same-origin', cache:'no-store',
          headers:{'Content-Type':'application/json', Accept:'application/json', 'X-CSRF-Token':session?.token ?? csrf()},
          body:JSON.stringify(op.command), signal:controller.signal});
        if(response.redirected && new URL(response.url).pathname.startsWith('/auth/'))return {status:401};
        const body=await response.json().catch(()=>null);
        database?.report(response, body);
        return {status:response.status, body};
      } finally {clearTimeout(timeout);}
    },
  });
  const esc = text => String(text ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const safeUrl = value => {try {const url = new URL(value); return ['http:','https:'].includes(url.protocol) ? esc(url.href) : '';} catch {return '';}};
  function lists(doc, target) {
    const abandoned = target.searchParams.get('abandoned') === 'true';
    const items = [...state.ideas].reverse().filter(item => item.status === (abandoned ? 'abandoned' : 'pending'));
    const host = doc.querySelector('[data-ideas-list]'); if (!host) return;
    doc.querySelector('[data-ideas-count]').textContent = `${abandoned ? '已放弃' : '待安排事项'} · ${items.length}`;
    host.innerHTML = items.map(item => `<article class="card" style="margin-block:16px;padding:20px"><h3 style="overflow-wrap:anywhere">${esc(item.title)}</h3>` +
      (item.note ? `<p style="white-space:pre-wrap;overflow-wrap:anywhere">${esc(item.note)}</p>` : '') +
      '<ul>' + item.reference_links.filter(link => safeUrl(link.url)).map(link => `<li><a href="${safeUrl(link.url)}" target="_blank" rel="noopener noreferrer">${esc(link.title || link.url)}</a></li>`).join('') + '</ul>' +
      (abandoned ? '' : `<div class="settings-actions" style="flex-wrap:wrap">${state.scheduled?.[item.id]
        ? `<a class="btn secondary" href="/my-schedule/events/${esc(state.scheduled[item.id])}">已创建日程 · 查看</a>`
        : `<a class="btn" href="/my-schedule/ideas/${esc(item.id)}/arrange">正式安排时间</a>`}` +
        `<a class="btn secondary" href="/my-schedule/ideas?edit=${esc(item.id)}">修改 idea</a>` +
        `<form method="post" action="/my-schedule/ideas/${esc(item.id)}/abandon"><input type="hidden" name="csrf_token" value="${esc(csrf())}"><button class="btn secondary" type="submit">放弃</button></form></div>`) + '</article>').join('') || '<p class="muted-text">还没有此类事项。</p>';
  }
  function statusUI() {
    if (location.pathname !== '/my-schedule/ideas') return;
    const host = document.querySelector('[data-ideas-list]'); if (!host || !state) return;
    let status = document.querySelector('[data-ideas-sync-status]');
    if (!status) {status = document.createElement('div'); status.dataset.ideasSyncStatus=''; status.setAttribute('role','status'); host.before(status);}
    status.replaceChildren();
    if (localError) status.append(document.createTextNode(localError));
    const ops = state.outbox || [];
    if (ops.some(op => op.state === 'pending')) status.append(document.createTextNode('已保存在本机，正在等待后台同步。'));
    for (const op of ops.filter(op => op.state !== 'pending')) {
      const row = document.createElement('p');
      row.textContent = `${op.command.title || '事项修改'}：${op.error || '等待重新登录'} `;
      const retry = document.createElement('button'); retry.className='btn secondary'; retry.textContent='重试';
      retry.onclick = async () => {
        try {await transaction(s => {s.outbox.filter(x => x.command.idea_id === op.command.idea_id).forEach(x => {x.state='pending'; x.next_attempt=0;}); return s;}); changed(); engine.wake(0);}
        catch (error) {localError=error.message; statusUI();}
      };
      const edit = document.createElement('a'); edit.className='btn secondary'; edit.textContent='修改草稿'; edit.href='/my-schedule/ideas?edit='+op.command.idea_id;
      const discard = document.createElement('button'); discard.className='btn secondary'; discard.textContent='放弃本次更改';
      discard.onclick = async () => {await transaction(s => ({...s, outbox:s.outbox.filter(x => x.command.idea_id !== op.command.idea_id)})); changed();};
      row.append(retry,edit,discard); status.append(row);
    }
  }
  function changed(broadcast = true) {
    if (stopped) return;
    if (location.pathname === '/my-schedule/ideas' && state) lists(document, new URL(location.href));
    statusUI(); document.dispatchEvent(new Event('personal:ideas-changed'));
    if (broadcast) channel?.postMessage({type:'changed',scope});
    void projectArranged();
  }
  async function projectArranged() {
    const current=state, calendar=window.awesomeInterviewCalendarData?.snapshot?.();
    if(!current||!calendar||stopped)return;
    const ids=new Set(calendar.events.map(event=>event.id)), scheduled={};
    for(const item of current.ideas){
      const id=await queue.eventId(`idea:${scope}:${item.id}`);
      if(ids.has(id))scheduled[item.id]=id;
    }
    if(stopped||state!==current||JSON.stringify(scheduled)===JSON.stringify(current.scheduled||{}))return;
    state.scheduled=scheduled;
    if(location.pathname==='/my-schedule/ideas')lists(document,new URL(location.href));
    document.dispatchEvent(new Event('personal:ideas-changed'));
  }
  async function page(url) {
    const target = new URL(url, location.href);
    const arranging=target.pathname.match(/^\/my-schedule\/ideas\/([^/]+)\/arrange$/);
    if (target.pathname !== '/my-schedule/ideas' && !arranging) return null;
    await ensure(); if (!state || stopped) return null;
    const workspace = window.awesomeInterviewCalendarModel;
    if(arranging){
      const item=state.ideas.find(item=>item.id===arranging[1]&&item.status==='pending');
      if (!item) throw Error('事项不存在或已放弃');
      const doc = workspace.localPage('local-event-form-template', '安排日程 - Awesome Schedule', '/my-schedule/new');
      if (!doc) return null;
      const form = doc.querySelector('#personal-event-form');
      const set=(name,value)=>{const input=form.querySelector(`[name=${name}]`);if(input){input.setAttribute('value',value);if(input.tagName==='TEXTAREA')input.textContent=value;}};
      set('title',item.title);set('note',item.note);
      const hidden=doc.createElement('input');hidden.type='hidden';hidden.name='source_idea_id';hidden.setAttribute('value',item.id);form.append(hidden);
      const day=window.awesomeInterviewCalendarModel.parts(Date.now()).day;
      set('start_at',day+'T'+(document.querySelector('meta[name="workday-start"]')?.content||'08:00'));
      set('end_at',day+'T'+(document.querySelector('meta[name="workday-end"]')?.content||'18:00'));
      const rows=form.querySelector('[data-reference-rows]'),template=rows.firstElementChild.cloneNode(true);rows.replaceChildren();
      for(const link of item.reference_links.length?item.reference_links:[{title:'',url:''}]){
        const row=template.cloneNode(true);for(const name of ['title','url'])row.querySelector(`[name=reference_${name}]`).setAttribute('value',link[name]);rows.append(row);
      }
      doc.querySelectorAll('[name=csrf_token]').forEach(input=>input.setAttribute('value',csrf()));
      return doc;
    }
    const doc = workspace.localPage('local-ideas-template', '待安排 - Awesome Schedule', '/my-schedule');
    if (!doc) return null;
    lists(doc,target);
    const form = doc.querySelector('[data-idea-form]'), abandoned = target.searchParams.get('abandoned') === 'true';
    if (abandoned) {doc.querySelector('[data-idea-form-heading]')?.remove(); form?.remove();}
    else {
      const id = target.searchParams.get('edit'), draft = state.outbox.find(op => op.state === 'failed' && op.command.idea_id === id);
      const item = draft ? {...draft.command, id, status:'pending'} : state.ideas.find(item => item.id === id);
      if (id && (!item || item.status !== 'pending')) throw Error('事项不存在或已放弃');
      if (item) {
        doc.querySelector('[data-idea-form-heading]').textContent='修改 idea';
        const hidden=doc.createElement('input'); hidden.name='idea_id'; hidden.type='hidden'; hidden.value=item.id; hidden.setAttribute('value',item.id); form.append(hidden);
        const title=form.querySelector('[name=title]'); title.value=item.title; title.setAttribute('value',item.title);
        form.querySelector('[name=note]').textContent=item.note;
        const rows=form.querySelector('[data-reference-rows]'), template=rows.firstElementChild.cloneNode(true); rows.replaceChildren();
        for (const link of item.reference_links.length ? item.reference_links : [{title:'',url:''}]) {
          const row=template.cloneNode(true); for (const name of ['title','url']) row.querySelector(`[name=reference_${name}]`).setAttribute('value',link[name]); rows.append(row);
        }
        form.querySelector('button[type=submit]').textContent='保存修改';
      }
    }
    doc.querySelectorAll('[name=csrf_token]').forEach(input => input.setAttribute('value',csrf()));
    for (const link of doc.querySelectorAll('nav[aria-label="事项分类"] a')) {
      const selected = link.getAttribute('href').includes('abandoned') === abandoned;
      link.classList.toggle('secondary',!selected); if (selected) link.setAttribute('aria-current','page'); else link.removeAttribute('aria-current');
    }
    void refresh(); return doc;
  }
  async function enqueue(command, id = crypto.randomUUID()) {
    await ensure(); if (!state) throw Error('请联网加载一次待安排后再保存。');
    if (command.action === 'create') command.idea_id = await queue.eventId(`idea-create:${scope}:${id}`);
    const failed = state.outbox.find(op => op.state === 'failed' && op.command.idea_id === command.idea_id && op.command.action === 'create');
    if (failed && command.action === 'edit') {id=failed.id; command.action='create';}
    await transaction(s => {
      if (s.outbox.length >= 200) throw Error('待同步操作较多，请先联网同步。');
      s.outbox=s.outbox.filter(op => !(op.state === 'failed' && op.command.idea_id === command.idea_id));
      s.outbox.push({id,command,created_at:new Date().toISOString(),state:'pending',attempts:0,next_attempt:0}); return s;
    });
    localError=''; changed(); engine.wake();
  }
  async function flushForIdea(id) {
    await hydrate(); await engine.flush();
    const latest = await transaction();
    const operations = latest?.outbox?.filter(op => op.command.idea_id === id) || [];
    const failed = operations.find(op => op.state === 'failed');
    if (failed) {
      return {status: 422, body: {detail: `关联事项未能保存，请先处理待安排草稿。 ${failed.error || ''}`.trim()}};
    }
    if (operations.some(op => op.state === 'auth')) return {status: 401};
    if (operations.length) return {status: 0, waiting: '等待关联事项同步，日程已保存在本机。'};
    return null;
  }
  document.addEventListener('submit', event => {
    const form=event.target, url=new URL(form.action,location.href);
    if (form.method?.toUpperCase() !== 'POST' || !(url.pathname === '/my-schedule/ideas' || /^\/my-schedule\/ideas\/[^/]+\/abandon$/.test(url.pathname))) return;
    event.preventDefault(); event.stopImmediatePropagation();
    if (form.dataset.ideaSaving) return; form.dataset.ideaSaving='1';
    const data=new FormData(form), id=data.get('idea_id'), abandon=url.pathname.endsWith('/abandon');
    const titles=data.getAll('reference_title'), links=data.getAll('reference_url');
    const command=abandon ? {action:'abandon',idea_id:url.pathname.split('/')[3]} : {
      action:id?'edit':'create', idea_id:id, title:String(data.get('title') || '').trim(), note:String(data.get('note') || ''),
      reference_links:links.map((url,index) => ({url:String(url).trim(),title:String(titles[index] || '').trim()})).filter(link => link.url || link.title)};
    void (async () => {
      try {
        if (!abandon && (!command.title || command.title.length>200 || command.note.length>20000 || command.reference_links.length>20 || command.reference_links.some(link => !safeUrl(link.url)))) throw Error('请检查标题、备注和参考链接。');
        await enqueue(command);
        window.awesomeInterviewNavigate('/my-schedule/ideas');
      } catch (error) {localError=error.message; statusUI();}
      finally {delete form.dataset.ideaSaving;}
    })();
  },true);
  function forget() {stopped=true; revision++; engine.stop(); state=null; void transaction(null,true).catch(()=>{});}
  document.addEventListener('submit', event => {if (new URL(event.target.action,location.href).pathname==='/auth/logout') {channel?.postMessage({type:'logout',scope}); forget();}});
  document.addEventListener('awesome-interview:session-expired', () => {
    engine.stop(); localError='登录已过期，本机待同步修改已保留，请重新登录后同步。'; statusUI();
  });
  channel?.addEventListener('message', async event => {
    if (event.data?.scope!==scope) return;
    if (event.data.type==='logout') {forget(); return;}
    const saved=await transaction(); if (!stopped) {state=saved ? model.rebase(saved) : null; changed(false); engine.wake();}
  });
  const start=async () => {await ensure(); changed(false); engine.wake(); void refresh();};
  document.addEventListener('personal:navigated',start);
  document.addEventListener('personal:database-restored', () => {lastPull=0; engine.wake(0); void refresh();});
  document.addEventListener('personal:calendar-mutated',()=>{void projectArranged();});
  document.addEventListener('visibilitychange', () => {if (!document.hidden) {void refresh(); engine.wake(0);}});
  window.addEventListener('online', () => {lastPull=0; engine.wake(0); void refresh();});
  window.awesomeInterviewIdeaData={ensure,refresh,page,enqueue,flushForIdea,
    snapshot:() => state, hasPending:() => !!state?.outbox?.length};
  void start();
})();
