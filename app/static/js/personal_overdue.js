(() => {
  let deadlineTimer;
  function watchDeadline() {
    clearTimeout(deadlineTimer);
    const deadline = Date.parse(document.querySelector('[data-next-deadline]')?.dataset.nextDeadline || '');
    if (Number.isFinite(deadline)) deadlineTimer = setTimeout(async () => {
      await window.awesomeInterviewRefreshCalendar?.();
      watchDeadline();
    }, Math.min(2147483647, deadline <= Date.now() ? 60000 : deadline - Date.now() + 1000));
  }
  document.addEventListener('DOMContentLoaded', watchDeadline);
  document.addEventListener('personal:navigated', watchDeadline);
  document.addEventListener('click', event => {
    const open = event.target.closest('[data-defer-open]');
    if (open) {
      const dialog = document.getElementById(open.dataset.deferOpen);
      const input = dialog.querySelector('[name="end_at"]');
      const now = new Date(Date.now() + 8 * 3600000 + 60000).toISOString().slice(0, 16);
      input.min = input.min > now ? input.min : now;
      input.value = input.min;
      input.dispatchEvent(new Event('change', {bubbles: true}));
      dialog.showModal();
    }
    if (event.target.closest('[data-defer-close]')) event.target.closest('dialog').close();
  });
  document.addEventListener('submit', async event => {
    const form = event.target;
    if (!form.matches('[data-defer-form]') || window.awesomeInterviewCalendarData?.handlesMutation(form)) return;
    event.preventDefault();
    const button = form.querySelector('[type="submit"]');
    const error = form.querySelector('[data-defer-error]');
    button.disabled = true;
    error.textContent = '';
    try {
      const response = await fetch(form.getAttribute('action'), {method: 'POST', body: new FormData(form), credentials: 'same-origin'});
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(typeof body.detail === 'string' ? body.detail : '延期失败，请稍后重试');
      }
      location.assign(response.url);
    } catch (reason) { error.textContent = reason.message || '网络异常，请重试'; }
    finally { button.disabled = false; }
  });
})();
