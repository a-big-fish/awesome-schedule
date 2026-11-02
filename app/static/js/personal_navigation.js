(() => {
  document.addEventListener('personal:unavailable', () => {
    if (document.querySelector('.personal-connection-notice')) return;
    const notice = document.createElement('p');
    notice.className = 'personal-connection-notice';
    notice.setAttribute('role', 'status');
    notice.textContent = '连接暂时不可用，当前页面已保留。网络恢复后请稍后再次点击目标页面。';
    document.querySelector('.page > main')?.prepend(notice);
  });
  document.addEventListener('personal:navigated', () => document.querySelector('.personal-connection-notice')?.remove());
  const header = document.querySelector('.personal-app .page-header');
  const button = header?.querySelector('[data-mobile-nav-toggle]');
  if (!header || !button) return;

  // Collapsing the menu changes page height and can move scrollY back across
  // the threshold. Arm each expansion once instead of reopening on scroll.
  let collapseAt = window.scrollY < 96 ? 96 : null;
  const setOpen = (open) => {
    header.dataset.mobileNavOpen = String(open);
    button.setAttribute('aria-expanded', String(open));
    button.setAttribute('aria-label', open ? '收起导航' : '展开导航');
  };
  setOpen(window.scrollY < 96);
  button.addEventListener('click', () => {
    const open = header.dataset.mobileNavOpen !== 'true';
    collapseAt = open ? window.scrollY + 96 : null;
    setOpen(open);
  });
  window.addEventListener('scroll', () => {
    if (window.matchMedia('(max-width: 1000px)').matches &&
        collapseAt !== null && window.scrollY >= collapseAt) {
      collapseAt = null;
      setOpen(false);
    }
  }, { passive: true });
  header.querySelectorAll('.page-nav a').forEach((link) => {
    link.addEventListener('click', () => { collapseAt = null; setOpen(false); });
  });
  document.addEventListener('personal:navigated', () => {
    collapseAt = 96;
    setOpen(true);
  });
})();
