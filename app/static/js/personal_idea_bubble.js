(() => {
  const bubble = document.querySelector('[data-idea-bubble]');
  if (!bubble) return;
  let snapshot = null, painted = null, paintRevision = 0;
  const onCalendar = () => location.pathname.replace(/\/$/, '') === '/my-schedule';
  const close = () => { bubble.open = false; };
  function paint() {
    bubble.hidden = !onCalendar() || !snapshot;
    if (bubble.hidden) close();
    if (!snapshot || snapshot === painted) return;
    painted = snapshot;
    const empty = snapshot.total === 0;
    bubble.querySelector('[data-idea-label]').textContent = empty ? '添加idea' : '待安排';
    bubble.querySelector('[data-idea-count]').hidden = empty;
    bubble.querySelector('[data-idea-count]').textContent = snapshot.total > 99 ? '99+' : snapshot.total;
    bubble.querySelector('summary').setAttribute('aria-label', empty
      ? '添加 idea，还没有待安排的想法，点击展开'
      : `待安排事项，共 ${snapshot.total} 条，点击展开`);
    bubble.querySelector('[data-idea-heading]').textContent = empty ? '还没有待安排的想法' : '还没安排时间的想法';
    bubble.querySelector('[data-idea-hint]').hidden = empty;
    bubble.querySelector('[data-idea-cta]').textContent = empty ? '记录 idea' : '查看全部待安排';
    bubble.querySelector('[data-idea-preview]').textContent = empty ? '先记下一个想法' : (snapshot.items[0]?.title || '');
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
    const version=++paintRevision;
    if (!onCalendar()) {paint(); return;}
    const api = window.awesomeInterviewIdeaData;
    if (!api) return;
    const data = await api.ensure();
    if (!data) { snapshot = null; paint(); return; }
    const calendar = window.awesomeInterviewCalendarData?.snapshot?.();
    const eventIds = new Set((calendar?.events || []).map(event => event.id));
    const arranged = new Set();
    for (const item of data.ideas) {
      const id = await window.awesomeInterviewCalendarOutbox.eventId(`idea:${data.user_id}:${item.id}`);
      if (calendar ? eventIds.has(id) : data.scheduled?.[item.id]) arranged.add(item.id);
    }
    const items = [...data.ideas].reverse().filter(item => item.status === 'pending' && !arranged.has(item.id));
    if(version!==paintRevision)return;
    snapshot = {total: items.length, items: items.slice(0,5)};
    paint();
  }
  document.addEventListener('personal:ideas-changed', refresh);
  document.addEventListener('personal:calendar-mutated', refresh);
  document.addEventListener('personal:navigated', refresh);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
  bubble.querySelector('[data-idea-close]').addEventListener('click', () => {
    close(); bubble.querySelector('summary').focus();
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && bubble.open) { close(); bubble.querySelector('summary').focus(); }
  });
  document.addEventListener('click', event => { if (!bubble.contains(event.target)) close(); });
  document.addEventListener('DOMContentLoaded', refresh);
})();
