(() => {
  const key = 'awesome-interview-calendar-completed';
  function init() {
    const calendar = document.querySelector('.personal-calendar[data-calendar-completed]');
    if (!calendar) return;
    const button = calendar.querySelector('[data-calendar-toggle]');
    const apply = (incomplete) => {
      calendar.classList.toggle('hide-completed', incomplete);
      button.textContent = incomplete ? '切换全部日程' : '切换未完成日程';
      button.setAttribute('aria-pressed', String(incomplete));
    };
    let stored;
    try { stored = sessionStorage.getItem(key); } catch (_) { /* Private mode. */ }
    apply(stored === null || stored === undefined
      ? calendar.dataset.calendarCompleted === 'incomplete' : stored === 'incomplete');
    button.addEventListener('click', () => {
      const incomplete = !calendar.classList.contains('hide-completed');
      apply(incomplete);
      try { sessionStorage.setItem(key, incomplete ? 'incomplete' : 'all'); } catch (_) { /* Private mode. */ }
    });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
  document.addEventListener('personal:navigated', init);
})();
