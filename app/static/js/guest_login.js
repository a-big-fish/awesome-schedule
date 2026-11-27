(() => {
  const dialog = document.querySelector('[data-login-modal]');
  const content = dialog?.querySelector('[data-login-content]');
  if (!dialog || !content) return;
  const calendarPath = '/my-schedule';
  let loading = false, destination = calendarPath;
  async function open(next = calendarPath) {
    destination = next;
    if (!dialog.open) dialog.showModal();
    const existing = content.querySelector('form');
    if (existing) { existing.querySelector('[name="next"]').value = destination; return; }
    if (loading) return;
    loading = true;
    content.textContent = '正在准备登录表单…';
    try {
      const response = await fetch(`/auth/login?fragment=1&next=${encodeURIComponent(destination)}`, {
        credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(10000)
      });
      if (new URL(response.url).pathname !== '/auth/login') {
        location.assign(response.url); return;
      }
      if (!response.ok) throw new Error();
      const parsed = new DOMParser().parseFromString(await response.text(), 'text/html');
      const panel = parsed.querySelector('.auth-login-panel');
      if (!panel) throw new Error();
      content.replaceChildren(panel);
      panel.querySelector('[name="next"]').value = destination;
      if (dialog.open) panel.querySelector('[name="identifier"]').focus();
    } catch {
      content.textContent = '登录表单加载失败，请关闭后重试。';
    } finally { loading = false; }
  }
  document.querySelector('[data-login-close]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', event => {if (event.target === dialog) dialog.close();});
  document.addEventListener('click', event => {
    const link = event.target.closest('a[href]');
    if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const target = new URL(link.href, location.href);
    if (target.origin !== location.origin) return;
    const protectedPage = target.pathname.startsWith('/my-schedule') &&
      !['/my-schedule', '/my-schedule/experiences/plaza'].includes(target.pathname.replace(/\/$/, ''));
    if (protectedPage || link.matches('[data-login-open]')) {
      event.preventDefault(); event.stopImmediatePropagation();
      open(protectedPage && !target.pathname.includes('/events/demo-') ? target.pathname + target.search : calendarPath);
    }
  }, true);
  document.addEventListener('submit', event => {
    if (event.target.matches('.auth-login-form')) return;
    if (event.target.method?.toLowerCase() === 'post') {
      event.preventDefault(); event.stopImmediatePropagation(); open(calendarPath);
    }
  }, true);
})();
