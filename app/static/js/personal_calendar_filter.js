(() => {
  let incompletePreference = true;
  const boundCalendars = new WeakSet();
  function packOverlaps(elements, bounds, place) {
    const rows = elements.map((element) => ({element, ...bounds(element)}))
      .sort((a, b) => a.start - b.start || b.end - a.end);
    let group = [], lanes = [];
    const finish = () => {
      const width = Math.max(1, lanes.length);
      group.forEach((item) => place(item.element, item.lane, width));
      group = []; lanes = [];
    };
    for (const item of rows) {
      if (group.length && group.every((prior) => prior.end <= item.start)) finish();
      let lane = lanes.findIndex((end) => end <= item.start);
      if (lane < 0) lane = lanes.length;
      lanes[lane] = item.end;
      item.lane = lane;
      group.push(item);
    }
    finish();
  }
  function repack(calendar) {
    const visible = (selector) => [...calendar.querySelectorAll(selector)]
      .filter((element) => !calendar.classList.contains('hide-completed') || (!element.classList.contains('is-completed') && !element.classList.contains('is-cancelled')));
    packOverlaps(visible('.time-grid .event-card'), (element) => {
      const start = Number(element.style.getPropertyValue('--start-min'));
      return {start, end: start + Number(element.style.getPropertyValue('--duration'))};
    }, (element, lane, width) => {
      element.style.setProperty('--column', String(lane + 1));
      element.style.setProperty('--columns', String(width));
    });
    calendar.querySelectorAll('.personal-week-day').forEach((day) => {
      packOverlaps([...day.querySelectorAll('.personal-week-event')].filter((element) =>
        !calendar.classList.contains('hide-completed') || (!element.classList.contains('is-completed') && !element.classList.contains('is-cancelled'))),
      (element) => {
        const start = parseFloat(element.style.top);
        return {start, end: start + parseFloat(element.style.height)};
      }, (element, lane, width) => {
        element.style.left = `calc(${lane * 100 / width}% + 2px)`;
        element.style.width = `calc(${100 / width}% - 4px)`;
      });
    });
    const days = [...calendar.querySelectorAll('.personal-period-month .personal-period-day')];
    for (let offset = 0; offset < days.length; offset += 7) {
      const week = days.slice(offset, offset + 7);
      const items = week.flatMap((day) => [...day.querySelectorAll('.personal-period-event')]);
      const active = items.filter((item) =>
        !calendar.classList.contains('hide-completed') || (!item.classList.contains('is-completed') && !item.classList.contains('is-cancelled')));
      const lanes = [...new Set(active.map((item) => Number(item.dataset.originalLane)))].sort((a, b) => a - b);
      active.forEach((item) => { item.style.gridRow = String(lanes.indexOf(Number(item.dataset.originalLane)) + 1); });
      week.forEach((day) => { day.querySelector('.personal-period-events').style.gridTemplateRows =
        lanes.length ? `repeat(${lanes.length}, 20px)` : ''; });
    }
  }
  function keepDayTitlesVisible(calendar) {
    const wrap = calendar.querySelector('.time-grid-wrap');
    if (!wrap || !wrap.addEventListener) return;
    let pending = false;
    const update = () => {
      pending = false;
      if (!wrap.isConnected) return;
      const viewport = wrap.getBoundingClientRect();
      // Read all geometry before applying styles to avoid layout thrashing.
      const offsets = [...wrap.querySelectorAll('.event-card')].map((card) => {
        const content = card.querySelector('.personal-event-content');
        if (!content || !card.getClientRects().length) return null;
        const rect = card.getBoundingClientRect();
        const limit = Math.max(0, card.clientHeight - content.offsetHeight - 20);
        return {content, offset: Math.min(limit, Math.max(0, viewport.top - rect.top + 10))};
      });
      offsets.forEach((item) => {
        if (item) item.content.style.setProperty('--content-offset', `${item.offset}px`);
      });
    };
    const schedule = () => {
      if (!pending) { pending = true; requestAnimationFrame(update); }
    };
    wrap.addEventListener('scroll', schedule, {passive: true});
    calendar.addEventListener('click', schedule);
    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(schedule).observe(wrap);
    schedule();
  }
  function init() {
    const calendar = document.querySelector('.personal-calendar[data-calendar-completed]');
    if (!calendar || boundCalendars.has(calendar)) return;
    boundCalendars.add(calendar);
    const button = calendar.querySelector('[data-calendar-toggle]');
    calendar.querySelectorAll('.personal-period-month .personal-period-event').forEach((item) => {
      item.dataset.originalLane ||= item.style.gridRow;
    });
    const apply = (incomplete) => {
      calendar.classList.toggle('hide-completed', incomplete);
      repack(calendar);
      button.textContent = incomplete ? '切换全部日程' : '切换未完成日程';
      button.setAttribute('aria-pressed', String(incomplete));
    };
    apply(incompletePreference);
    keepDayTitlesVisible(calendar);
    button.addEventListener('click', () => {
      const incomplete = !calendar.classList.contains('hide-completed');
      apply(incomplete);
      incompletePreference = incomplete;
    });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
  document.addEventListener('personal:navigated', init);
})();
