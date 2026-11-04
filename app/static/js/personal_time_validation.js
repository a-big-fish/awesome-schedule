(() => {
  function confirmWeekend(form, startValue, endValue) {
    const skip = form.querySelector('[data-skip-weekends]');
    const repeat = form.querySelector('[data-repeat-enabled]');
    if (!skip?.checked || skip.disabled || repeat?.checked || !startValue || !endValue || endValue <= startValue) return true;
    const weekday = value => new Date(`${value.slice(0, 10)}T00:00:00Z`).getUTCDay();
    if (![1, 2, 3, 4, 5].includes(weekday(startValue)) || ![0, 6].includes(weekday(endValue))) return true;
    return window.confirm('是否确认周末不展示日程？');
  }
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
    if (form && event.target.name === 'skip_weekends' && event.target.checked) {
      const start = form.querySelector('[name="start_at"], [name="reviewed_start"]');
      const end = form.querySelector('[name="end_at"], [name="reviewed_end"]');
      if (start && end && !confirmWeekend(form, start.value, end.value)) event.target.checked = false;
    }
    if (form && ['start_at', 'end_at', 'reviewed_start', 'reviewed_end', 'repeat_enabled'].includes(event.target.name)) validate(form);
  });
  document.addEventListener('submit', event => {
    const form = event.target;
    const valid = validate(form);
    const start = form.querySelector('[name="start_at"], [name="reviewed_start"]');
    const end = form.querySelector('[name="end_at"], [name="reviewed_end"]');
    if (!valid || (start && end && !confirmWeekend(form, start.value, end.value))) {
      event.preventDefault();
      event.stopImmediatePropagation();
      event.target.querySelector('[name="end_at"], [name="reviewed_end"]')?.closest('.personal-time-field')?.querySelector('[data-time-trigger]')?.focus();
    }
  }, true);
})();
