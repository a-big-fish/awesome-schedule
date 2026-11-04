(() => {
  function validate(form) {
    const start = form.querySelector('[name="start_at"], [name="reviewed_start"]');
    const end = form.querySelector('[name="end_at"], [name="reviewed_end"]');
    if (!start || !end) return true;
    let message = '';
    if (start.value && end.value && end.value <= start.value) message = '结束时间必须晚于开始时间';
    const repeat = form.querySelector('[data-repeat-enabled]');
    if (!message && repeat?.checked && start.value && end.value && start.value.slice(0, 10) !== end.value.slice(0, 10))
      message = '重复任务的开始和结束时间必须在同一天，不允许跨天';
    let error = form.querySelector('[data-period-error]');
    if (message && !error) {
      error = document.createElement('p');
      error.dataset.periodError = '';
      error.className = 'parse-tip error';
      error.setAttribute('role', 'alert');
      const pair = start.closest('.personal-form-pair');
      if (pair) pair.after(error); else form.append(error);
    }
    if (error) { error.textContent = message; error.hidden = !message; }
    const trigger = end.closest('.personal-time-field')?.querySelector('[data-time-trigger]');
    if (trigger) trigger.setAttribute('aria-invalid', String(Boolean(message)));
    return !message;
  }
  document.addEventListener('change', event => {
    const form = event.target.closest('form');
    if (form && ['start_at', 'end_at', 'reviewed_start', 'reviewed_end', 'repeat_enabled'].includes(event.target.name)) validate(form);
  });
  document.addEventListener('submit', event => {
    if (!validate(event.target)) {
      event.preventDefault();
      event.stopImmediatePropagation();
      event.target.querySelector('[name="end_at"], [name="reviewed_end"]')?.closest('.personal-time-field')?.querySelector('[data-time-trigger]')?.focus();
    }
  }, true);
})();
