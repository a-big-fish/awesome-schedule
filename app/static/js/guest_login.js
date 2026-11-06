(() => {
  const dialog = document.querySelector('[data-login-modal]');
  const frame = dialog?.querySelector('[data-login-frame]');
  if (!dialog || !frame) return;
  function open(next) {
    frame.src = `/auth/login?embed=1&next=${encodeURIComponent(next || '/my-schedule/experiences/plaza')}`;
    if (!dialog.open) dialog.showModal();
  }
  document.querySelector('[data-login-close]').addEventListener('click', () => dialog.close());
  window.addEventListener('message', event => {
    if (event.origin === location.origin && event.source === frame.contentWindow &&
        event.data?.type === 'awesome-interview:close-login') dialog.close();
  });
  dialog.addEventListener('click', event => {if (event.target === dialog) dialog.close();});
  frame.addEventListener('load', () => {
    try {
      const url = new URL(frame.contentWindow.location.href);
      if (url.origin === location.origin && url.pathname.startsWith('/my-schedule')) location.assign(url.href);
    } catch { /* Same-origin login only. */ }
  });
  document.addEventListener('click', event => {
    const link = event.target.closest('a[href]');
    if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const target = new URL(link.href, location.href);
    if (target.origin !== location.origin) return;
    const protectedPage = target.pathname.startsWith('/my-schedule') &&
      !['/my-schedule', '/my-schedule/experiences/plaza'].includes(target.pathname.replace(/\/$/, ''));
    if (protectedPage || link.matches('[data-login-open]')) {
      event.preventDefault(); event.stopImmediatePropagation();
      open(protectedPage && !target.pathname.includes('/events/demo-') ? target.pathname + target.search : '/my-schedule');
    }
  }, true);
  document.addEventListener('submit', event => {
    if (event.target.method?.toLowerCase() === 'post') {
      event.preventDefault(); event.stopImmediatePropagation(); open('/my-schedule');
    }
  }, true);
})();
