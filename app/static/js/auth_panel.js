(() => {
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && window.parent !== window && !document.querySelector('dialog[open]')) {
      window.parent.postMessage({type: 'awesome-interview:close-login'}, location.origin);
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
