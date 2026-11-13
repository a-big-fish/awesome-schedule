/* Durable outbox state transitions. UI and transport are injected, so retries are testable. */
(() => {
  const copy = value => structuredClone(value);
  const active = op => op.state !== 'failed';
  function rebase(state) {
    const events = new Map((state.confirmed_events || state.events || []).map(e => [e.id, copy(e)]));
    for (const op of state.outbox || []) {
      if (!active(op)) continue;
      const c = op.command, id = c.event_id;
      if (c.action === 'delete') { events.delete(id); continue; }
      let e = events.get(id);
      if (c.action === 'create') e = {...copy(c.event), id, created_at:op.created_at, repeat_completions:{}, completed_at:null, cancelled_at:null};
      if (!e) continue;
      if (c.action === 'edit') e = {...e, ...copy(c.event)};
      if (c.action === 'completion') {
        if (e.repeat_weekdays?.length) {
          const day = c.occurrence_day;
          e.repeat_completions = {...e.repeat_completions};
          if (c.completed) e.repeat_completions[day] = op.created_at; else delete e.repeat_completions[day];
        } else e.completed_at = c.completed ? op.created_at : null;
      }
      if (c.action === 'defer') e.end_at = c.end_at;
      if (c.action === 'cancel') { e.cancelled_at = op.created_at; e.reminder_offsets_minutes = []; }
      if (c.action === 'close_repeat') e.repeat_closed_on = op.closed_on;
      e.updated_at = op.created_at;
      events.set(id, e);
    }
    state.events = [...events.values()];
    return state;
  }
  function acknowledge(state, op, reply) {
    if(!state.outbox.some(item=>item.id===op.id))return state;
    const base = new Map(state.confirmed_events.map(e => [e.id, e]));
    if (reply.deleted_id) base.delete(reply.deleted_id);
    else if (reply.event) base.set(reply.event.id, reply.event);
    state.confirmed_events = [...base.values()];
    state.outbox = state.outbox.filter(item => item.id !== op.id);
    state.last_modified_at = reply.server_time;
    return rebase(state);
  }
  function reject(state, op, message, status) {
    let found = false;
    for (const item of state.outbox) {
      if (item.id === op.id) found = true;
      if (found && item.command.event_id === op.command.event_id) {
        item.state = 'failed'; item.error = message; item.status = status;
      }
    }
    return rebase(state);
  }
  async function eventId(key) {
    const namespace = Uint8Array.from('6ba7b8119dad11d180b400c04fd430c8'.match(/../g), x => parseInt(x,16));
    const text = new TextEncoder().encode(key), bytes = new Uint8Array(namespace.length + text.length);
    bytes.set(namespace); bytes.set(text,namespace.length);
    const hash = new Uint8Array(await crypto.subtle.digest('SHA-1',bytes)).slice(0,16);
    hash[6] = (hash[6]&15)|80; hash[8] = (hash[8]&63)|128;
    const hex = [...hash].map(x=>x.toString(16).padStart(2,'0')).join('');
    return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
  }
  class SyncEngine {
    constructor({read, update, send, notify=()=>{}, lock=fn=>fn(), online=()=>true, now=()=>Date.now()}) {
      Object.assign(this,{read,update,send,notify,lock,online,now});
      this.running = false; this.stopped = false; this.timer = null;
    }
    wake(delay=200) {
      clearTimeout(this.timer);
      if (!this.stopped) this.timer = setTimeout(()=>this.flush().catch(()=>this.wake(5000)),delay);
    }
    stop() { this.stopped = true; clearTimeout(this.timer); }
    async flush() {
      if (this.running || this.stopped || !this.online()) return;
      this.running = true;
      try { await this.lock(async()=>{
        while (!this.stopped && this.online()) {
          const state = await this.read(), op = state?.outbox?.find(active);
          if (!op || op.state === 'auth') break;
          const wait = (op.next_attempt || 0) - this.now();
          if (wait > 0) { this.wake(wait); break; }
          let reply;
          try { reply = await this.send(op); }
          catch { reply = {status:0}; }
          if (this.stopped) break;
          if (reply.status >= 200 && reply.status < 300 && reply.body?.operation_id === op.id && reply.body?.user_id === state.user_id) {
            await this.update(s=>acknowledge(s,op,reply.body)); this.notify(); continue;
          }
          if ([401,403].includes(reply.status)) {
            await this.update(s=>{const item=s.outbox.find(x=>x.id===op.id);if(item){item.state='auth';item.error='登录已过期，请重新登录后同步。'}return s});
            this.notify(); break;
          }
          if ([400,404,409,422].includes(reply.status)) {
            const reason = typeof reply.body?.detail === 'string' ? reply.body.detail : '服务器拒绝了本次修改，请检查草稿。';
            await this.update(s=>reject(s,op,reason,reply.status)); this.notify(); continue;
          }
          // A timeout may follow a committed write: retain the SAME operation ID for retries.
          let delay=1000;
          await this.update(s=>{const item=s.outbox.find(x=>x.id===op.id);if(item){item.attempts=(item.attempts||0)+1;delay=Math.min(300000,1000*2**Math.min(item.attempts-1,9));item.next_attempt=this.now()+delay;item.error='网络暂不可用，已保存在本机，稍后自动重试。'}return s});
          this.notify(); this.wake(delay); break;
        }
      }); } finally { this.running=false; }
    }
  }
  const api = {rebase,acknowledge,reject,eventId,SyncEngine,active};
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else window.awesomeInterviewCalendarOutbox = api;
})();
