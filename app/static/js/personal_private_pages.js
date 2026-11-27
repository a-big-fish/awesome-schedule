/* Bounded, account-scoped private list snapshots. Never persist CSRF/session credentials. */
(() => {
  const owner = document.querySelector('meta[name="calendar-user"]')?.content;
  if (!owner || document.querySelector('meta[name="guest-browsing"]')) return;
  let stopped = false, epoch = 0;
  const memory = new Map();
  function store(action, key, value) {
    const version = epoch;
    return new Promise((resolve,reject) => {
      let db, tx, done=false;
      const finish=(error,result)=>{if(done)return;done=true;clearTimeout(timer);db?.close();error?reject(error):resolve(result);};
      const timer=setTimeout(()=>{tx?.abort();finish(Error('storage timeout'));},1000);
      let open;try {open=indexedDB.open('awesome-interview-private-pages-v1',1);} catch(error){finish(error);return;}
      open.onupgradeneeded=()=>open.result.createObjectStore('pages',{keyPath:'key'});
      open.onerror=()=>finish(open.error);open.onblocked=()=>finish(Error('blocked'));
      open.onsuccess=()=>{
        db=open.result;if(done){db.close();return;}
        if(version!==epoch||stopped&&action!=='clear'){finish(null);return;}
        tx=db.transaction('pages',action==='get'?'readonly':'readwrite');
        const table=tx.objectStore('pages');let result;
        const request=action==='get'?table.get(owner+'|'+key):action==='put'?table.put({key:owner+'|'+key,owner,...value}):table.openCursor();
        request.onsuccess=()=>{
          result=request.result;
          if(action==='clear'&&result){if(result.value.owner===owner)result.delete();result.continue();}
        };
        if(action==='put'){
          const cursor=table.openCursor(), entries=[];
          cursor.onsuccess=()=>{const row=cursor.result;if(row){if(row.value.owner===owner)entries.push(row.value);row.continue();}
            else entries.sort((a,b)=>b.time-a.time).slice(30).forEach(item=>table.delete(item.key));};
        }
        tx.oncomplete=()=>finish(null,result);tx.onabort=tx.onerror=()=>finish(tx.error||Error('storage'));
      };
    });
  }
  function scrub(html) {
    const doc=new DOMParser().parseFromString(html,'text/html');
    doc.querySelectorAll('[name="csrf_token"],meta[name="calendar-csrf"],meta[name="performance-csrf"]').forEach(node=>{
      node.removeAttribute('value');node.removeAttribute('content');
    });return doc.documentElement.outerHTML;
  }
  function clear(stop=false) {epoch++;memory.clear();stopped=stop;return store('clear').catch(()=>{});}
  window.awesomeInterviewPrivatePages={
    async get(key){if(stopped)return null;const version=epoch;const value=memory.get(key)||await store('get',key).catch(()=>null);return version===epoch&&!stopped?value:null;},
    async put(key,html){if(stopped)return;const value={html:scrub(html),time:Date.now()};memory.set(key,value);await store('put',key,value).catch(()=>{});},
    clear,
  };
  document.addEventListener('submit',event=>{
    if(event.target.method?.toUpperCase()==='POST')void clear(new URL(event.target.action,location.href).pathname==='/auth/logout');
  });
  document.addEventListener('awesome-interview:session-expired',()=>void clear(true));
  document.addEventListener('personal:calendar-mutated',()=>void clear());
  const channel=typeof BroadcastChannel==='function'?new BroadcastChannel('awesome-interview-pages'):null;
  channel?.addEventListener('message',()=>void clear());
})();
