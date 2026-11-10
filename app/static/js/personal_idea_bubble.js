(() => {
  const bubble = document.querySelector('[data-idea-bubble]');
  if (!bubble) return;
  let snapshot = null, painted = null, updated = 0, pending = null, generation = 0, attempted = -Infinity;
  const onCalendar = () => location.pathname.replace(/\/$/, '') === '/my-schedule';
  const close = () => { bubble.open = false; };
  function paint() {
    bubble.hidden = !onCalendar() || !snapshot?.total;
    if (bubble.hidden) close();
    if (!snapshot || snapshot === painted) return;
    painted = snapshot;
    bubble.querySelector('[data-idea-count]').textContent = snapshot.total;
    bubble.querySelector('[data-idea-preview]').textContent = snapshot.items[0]?.title || '';
    const list = bubble.querySelector('[data-idea-items]');
    list.replaceChildren();
    for (const item of snapshot.items) {
      const row = document.createElement('li');
      const title = document.createElement('strong');
      title.textContent = item.title;
      row.append(title);
      const actions = document.createElement('div');
      for (const [label, url] of [
        ['安排时间', `/my-schedule/ideas/${encodeURIComponent(item.id)}/arrange`],
        ['修改', `/my-schedule/ideas?edit=${encodeURIComponent(item.id)}`],
      ]) {
        const link = document.createElement('a');
        link.textContent = label; link.href = url;
        link.setAttribute('aria-label', `${label}：${item.title}`);
        actions.append(link);
      }
      row.append(actions); list.append(row);
    }
  }
  async function refresh() {
    paint();
    if (!onCalendar() || document.hidden || pending || Date.now() - attempted < 30000 ||
        (snapshot && Date.now() - updated < 60000)) return;
    attempted = Date.now();
    const version = generation;
    const controller = new AbortController();
    pending = controller;
    const timeout = setTimeout(() => controller.abort(), 5000);
    try {
      const response = await fetch('/my-schedule/ideas/summary', {
        credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
        headers: {Accept: 'application/json'},
      });
      if (response.status === 401 || response.status === 403 || response.redirected) {
        snapshot = null; paint(); return;
      }
      if (!response.ok) return;
      const data = await response.json();
      if (version !== generation || !Number.isInteger(data.total) || data.total < 0 ||
          !Array.isArray(data.items) || data.items.some(item => typeof item.id !== 'string' || typeof item.title !== 'string')) return;
      snapshot = {total: data.total, items: data.items.slice(0, 5)};
      updated = Date.now(); paint();
    } catch { /* Retain cached titles when offline; no polling or reload. */ }
    finally { clearTimeout(timeout); if (pending === controller) pending = null; }
  }
  const invalidate = () => {
    generation++; pending?.abort(); pending = null; snapshot = null; updated = 0; attempted = -Infinity; paint();
  };
  const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('awesome-interview-pages') : null;
  channel?.addEventListener('message', () => { invalidate(); refresh(); });
  document.addEventListener('personal:navigated', refresh);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
  document.addEventListener('submit', event => {
    if (event.target.method?.toUpperCase() === 'POST') invalidate();
  });
  bubble.querySelector('[data-idea-close]').addEventListener('click', () => {
    close(); bubble.querySelector('summary').focus();
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && bubble.open) { close(); bubble.querySelector('summary').focus(); }
  });
  document.addEventListener('click', event => { if (!bubble.contains(event.target)) close(); });
  setTimeout(refresh, 300);
})();
