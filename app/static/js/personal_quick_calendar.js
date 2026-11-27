(() => {
  const iso = date => date.toISOString().slice(0, 10);
  function monthDays(month) {
    const first = new Date(`${month.slice(0, 7)}-01T00:00:00Z`);
    first.setUTCDate(1 - (first.getUTCDay() + 6) % 7);
    return Array.from({length: 42}, (_, index) => {
      const day = new Date(first); day.setUTCDate(day.getUTCDate() + index); return iso(day);
    });
  }
  function shiftMonth(month, offset) {
    const date = new Date(`${month.slice(0, 7)}-01T00:00:00Z`);
    date.setUTCMonth(date.getUTCMonth() + offset); return iso(date).slice(0, 7);
  }
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = {monthDays, shiftMonth}; return;
  }
  const small = window.matchMedia('(max-width: 1150px)');
  const bound = new WeakSet();
  let desktopExpanded = true, mobileExpanded = false;
  function setExpanded(value) {
    if (small.matches) mobileExpanded = value; else desktopExpanded = value;
    const panel = document.querySelector('[data-quick-calendar]');
    const toggle = document.querySelector('[data-quick-calendar-toggle]');
    if (!panel || !toggle) return;
    const expanded = small.matches ? mobileExpanded : desktopExpanded;
    panel.hidden = !expanded;
    panel.closest('.personal-layout').classList.toggle('quick-calendar-collapsed', !expanded);
    toggle.setAttribute('aria-expanded', String(expanded));
    toggle.setAttribute('aria-label', expanded ? '收起快速跳转日历' : '展开快速跳转日历');
  }
  function init() {
    const panel = document.querySelector('[data-quick-calendar]');
    const calendar = document.querySelector('.personal-calendar[data-selected-date]');
    if (!panel || !calendar || bound.has(panel)) return;
    bound.add(panel);
    const selected = calendar.dataset.selectedDate;
    const view = calendar.dataset.selectedView || 'day';
    let month = selected.slice(0, 7);
    const today = new Intl.DateTimeFormat('sv-SE', {timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit'}).format(new Date());
    function render() {
      panel.querySelector('[data-mini-heading]').textContent = `${month.slice(0, 4)}年${Number(month.slice(5))}月`;
      const grid = panel.querySelector('[data-mini-days]'); grid.replaceChildren();
      for (const day of monthDays(month)) {
        const link = document.createElement('a');
        link.href = `/my-schedule?view=${view}&date_value=${day}`;
        link.textContent = Number(day.slice(8));
        link.setAttribute('aria-label', `查看 ${day} 的日程`);
        if (day === selected) { link.classList.add('is-selected'); link.setAttribute('aria-current', 'date'); }
        if (day === today) { link.classList.add('is-today'); link.title = '今天'; }
        if (!day.startsWith(month)) link.classList.add('outside-month');
        grid.append(link);
      }
      const todayLink = panel.querySelector('[data-mini-today]');
      todayLink.href = `/my-schedule?view=${view}&date_value=${today}`;
    }
    panel.addEventListener('click', event => {
      const step = event.target.closest('[data-mini-step]');
      if (step) { month = shiftMonth(month, Number(step.dataset.miniStep)); render(); }
      else if (small.matches && event.target.closest('a[href]')) setExpanded(false);
    });
    const toggle = document.querySelector('[data-quick-calendar-toggle]');
    toggle.addEventListener('click', () => setExpanded(!(small.matches ? mobileExpanded : desktopExpanded)));
    render(); setExpanded(small.matches ? mobileExpanded : desktopExpanded);
  }
  small.addEventListener('change', () => { mobileExpanded = false; setExpanded(small.matches ? false : desktopExpanded); });
  document.addEventListener('click', event => {
    if (small.matches && mobileExpanded && !event.target.closest('[data-quick-calendar], [data-quick-calendar-toggle]')) setExpanded(false);
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && small.matches && mobileExpanded) {
      setExpanded(false); document.querySelector('[data-quick-calendar-toggle]')?.focus();
    }
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
  document.addEventListener('personal:navigated', init);
})();
