(() => {
  document.addEventListener('submit', async event => {
    const form = event.target.closest('.auth-login-form');
    if (!form) return;
    event.preventDefault();
    if (form.dataset.submitting) return;
    form.dataset.submitting = 'true';
    const button = form.querySelector('button[type="submit"]');
    const error = form.closest('.auth-login-panel').querySelector('[data-login-error]');
    button.disabled = true; button.textContent = '正在登录…'; error.hidden = true;
    try {
      const response = await fetch(form.action, {method:'POST', body:new FormData(form),
        credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'},
        signal:AbortSignal.timeout(15000)});
      const result = await response.json();
      if (!response.ok) {
        if (result.csrf_token) form.querySelector('[name="csrf_token"]').value = result.csrf_token;
        throw new Error(result.detail || '登录失败，请重试');
      }
      const target = new URL(result.next, location.origin);
      if (target.origin !== location.origin || !target.pathname.startsWith('/my-schedule')) throw new Error('登录跳转无效');
      location.assign(target.href);
    } catch (failure) {
      error.textContent = failure.name === 'TimeoutError' ? '登录请求超时，请稍后重试' : failure.message || '网络异常，请重试';
      error.hidden = false;
      button.disabled = false; button.textContent = '登录'; delete form.dataset.submitting;
    }
  });
  document.addEventListener('click', event => {
    const button = event.target.closest('button');
    if (!button) return;
    const panel = document.querySelector('.auth-login-panel');
    if (!panel) return;
    if (button.matches('[data-auth-password], [data-auth-phone]')) {
      const phone = button.matches('[data-auth-phone]');
      panel.querySelector('[data-password-panel]').hidden = phone;
      panel.querySelector('[data-phone-panel]').hidden = !phone;
      panel.querySelectorAll('.auth-methods button').forEach(item => item.classList.toggle('is-active', item === button));
    }
    if (button.dataset.authUnavailable) {
      panel.querySelector('[data-auth-notice-text]').textContent = button.dataset.authUnavailable;
      panel.querySelector('.auth-notice-dialog').showModal();
    }
    if (button.matches('[data-auth-notice-close]')) panel.querySelector('.auth-notice-dialog').close();
  });
})();
