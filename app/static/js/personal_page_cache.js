(() => {
  // Private HTML stays in this page's memory; the server sends no-store.
  const paths = ['/my-schedule/new', '/my-schedule', '/my-schedule/experiences/plaza', '/my-schedule/experiences',
    '/my-schedule/profile', '/my-schedule/interviews'];
  const allowed = new Set(paths);
  if (!document.querySelector('.personal-app .page-nav a[href="/my-schedule/experiences/plaza"]')) return;
  const ttl = 5 * 60 * 1000;
  const calendarTtl = 18 * 60 * 60 * 1000;
  const pages = new Map();
  const calendarPages = new Map();
  const calendarLimit = 24;
  const pending = new Map();
  let cacheGeneration = 0;
  let navigationId = 0;
  let pageEdited = false;
  let unavailableUntil = 0;
  const unavailable = () => Object.assign(new Error('连接暂时不可用，请稍后重试'), {transient: true});
  const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('awesome-interview-pages') : null;
  const clearPages = () => { cacheGeneration++; pages.clear(); calendarPages.clear(); pending.clear(); };
  channel?.addEventListener('message', clearPages);
  const isCalendar = (target) => target.pathname === '/my-schedule' &&
    [...target.searchParams.keys()].every((key) => ['view', 'date_value', 'show_completed', 'show_all'].includes(key));
  const isEventPage = target => /^\/my-schedule\/events\/[^/]+(?:\/edit)?$/.test(target.pathname) && !target.search;
  const canNavigate = (target) => (allowed.has(target.pathname) && (!target.search || isCalendar(target))) || isEventPage(target);
  const today = () => {
    const parts = new Intl.DateTimeFormat('en-CA', {timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit'})
      .formatToParts(new Date()).reduce((result, part) => ({...result, [part.type]: part.value}), {});
    return `${parts.year}-${parts.month}-${parts.day}`;
  };
  const pageKey = (target) => {
    if (!isCalendar(target)) return target.pathname + target.search;
    // Default URLs, parameter order and the local completion filter share one snapshot.
    const query = new URLSearchParams();
    query.set('view', target.searchParams.get('view') || 'day');
    query.set('date_value', target.searchParams.get('date_value') || today());
    if (['true', '1', 'on', 'yes'].includes((target.searchParams.get('show_all') || '').toLowerCase())) query.set('show_all', 'true');
    return target.pathname + '?' + query.toString();
  };
  const isFresh = (stored, calendar) => {
    if (!stored || Date.now() - stored.time >= (calendar ? calendarTtl : ttl)) return false;
    const deadline = calendar && stored.html.match(/data-next-deadline="([^"]+)"/);
    return !deadline || Date.parse(deadline[1]) > Date.now();
  };

  async function load(url, force = false) {
    url = pageKey(new URL(url, location.href));
    const calendar = isCalendar(new URL(url, location.href));
    const cache = calendar ? calendarPages : pages;
    const stored = cache.get(url);
    if (!force && isFresh(stored, calendar)) return stored.html;
    if (pending.has(url)) return pending.get(url);
    if (Date.now() < unavailableUntil) throw unavailable();
    const generation = cacheGeneration;
    const task = (async () => {
      let response;
      try {
        response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/html' } });
      } catch {
        unavailableUntil = Date.now() + 30000;
        throw unavailable();
      }
      if (response.status >= 500) {
        unavailableUntil = Date.now() + 30000;
        throw unavailable();
      }
      const resolved = new URL(response.url, location.href);
      if (resolved.pathname.startsWith('/auth/login') || response.status === 401) {
        clearPages();
        document.dispatchEvent(new Event('awesome-interview:session-expired'));
        location.assign(resolved.pathname.startsWith('/auth/login') ? resolved.href : '/auth/login');
        throw new Error('登录已过期');
      }
      if (!response.ok || pageKey(resolved) !== url ||
          !response.headers.get('content-type')?.includes('text/html')) throw new Error('页面需要重新加载');
      const html = await response.text();
      if (generation === cacheGeneration) {
        cache.set(url, { html, time: Date.now() });
        if (calendar && cache.size > calendarLimit) cache.delete(cache.keys().next().value);
      }
      return html;
    })();
    pending.set(url, task);
    try { return await task; }
    finally { if (pending.get(url) === task) pending.delete(url); }
  }

  let prefetching = false;
  let prefetched = false;
  let calendarPrefetching = false;
  let calendarPrefetchTarget = null;
  function scheduleCalendarNeighbors() {
    const calendar = document.querySelector('.personal-calendar[data-selected-date]');
    if (!calendar || calendar.dataset.selectedView !== 'day') return;
    calendarPrefetchTarget = calendar.dataset.selectedDate;
    if (calendarPrefetching) return;
    calendarPrefetching = true;
    setTimeout(async () => {
      try {
        while (calendarPrefetchTarget) {
          const selected = calendarPrefetchTarget;
          calendarPrefetchTarget = null;
          const date = new Date(`${selected}T00:00:00Z`);
          if (Number.isNaN(date.getTime())) continue;
          for (const offset of [-1, 1]) {
            if (calendarPrefetchTarget || document.hidden) break;
            const neighbor = new Date(date);
            neighbor.setUTCDate(date.getUTCDate() + offset);
            const key = `/my-schedule?view=day&date_value=${neighbor.toISOString().slice(0, 10)}`;
            await load(key).catch(() => {});
          }
        }
      } finally { calendarPrefetching = false; }
    }, 150);
  }
  const prefetch = async () => {
    if (prefetching || prefetched || document.querySelector('[data-experience-editor]')) return;
    prefetched = true;
    prefetching = true;
    try {
      for (const path of ['/my-schedule/new', '/my-schedule']) {
        if (document.hidden || document.querySelector('[data-experience-editor]')) break;
        if (!document.querySelector(`.page-nav a[href="${path}"]`)) continue;
        if (path !== location.pathname) await load(path).catch(() => {});
      }
    } finally { prefetching = false; }
  };

  const setNavState = (page) => {
    const active = page.querySelector('.page-nav a[aria-current="page"]')?.getAttribute('href');
    document.querySelectorAll('.page-nav a').forEach((link) => {
      if (link.getAttribute('href') === active) link.setAttribute('aria-current', 'page');
      else link.removeAttribute('aria-current');
    });
  };

  function render(html, resetScroll) {
    const renderStarted = performance.now();
    const page = new DOMParser().parseFromString(html, 'text/html');
    const nextMain = page.querySelector('.page > main');
    const currentMain = document.querySelector('.page > main');
    if (!nextMain || !currentMain || !page.body.classList.contains('personal-app'))
      throw new Error('页面结构已变化');
    document.title = page.title;
    document.body.className = page.body.className;
    for (const name of ['workday-start', 'workday-end']) {
      const latest = page.querySelector(`meta[name="${name}"]`);
      const current = document.querySelector(`meta[name="${name}"]`);
      if (latest && current) current.content = latest.content;
    }
    currentMain.replaceWith(nextMain);
    setNavState(page);
    pageEdited = false;
    if (resetScroll) window.scrollTo(0, 0);
    document.dispatchEvent(new Event('personal:navigated'));
    scheduleCalendarNeighbors();
    requestAnimationFrame(() => requestAnimationFrame(() => {
      window.awesomeInterviewPerformance?.recordRender(performance.now() - renderStarted);
    }));
  }

  async function navigate(url, addHistory = true) {
    const target = new URL(url, location.href);
    if (target.origin !== location.origin || !canNavigate(target)) {
      location.assign(target.href);
      return;
    }
    const id = ++navigationId;
    const key = pageKey(target);
    const calendar = isCalendar(target);
    const stored = (calendar ? calendarPages : pages).get(key);
    const cached = stored?.html;
    if (cached) {
      try {
        if (addHistory) history.pushState({ personalPage: true }, '', key);
        render(cached, true);
      } catch {
        location.assign(target.href);
        return;
      }
      if (isFresh(stored, calendar)) return;
      // The cached page is visible before this request completes.
      load(key, true).then((html) => {
        if (id !== navigationId || pageKey(new URL(location.href)) !== key || pageEdited || html === cached)
          return;
        const focused = document.activeElement;
        if (focused?.matches('input, textarea, select, [contenteditable]')) return;
        try { render(html, false); } catch { location.assign(target.href); }
      }).catch(() => {});
      return;
    }
    document.body.classList.add('personal-navigation-loading');
    try {
      const html = await load(key);
      if (id !== navigationId) return;
      if (addHistory) history.pushState({ personalPage: true }, '', key);
      render(html, true);
      prefetch();
    } catch (error) {
      if (error.transient) document.dispatchEvent(new Event('personal:unavailable'));
      else location.assign(target.href);
    }
    finally { document.body.classList.remove('personal-navigation-loading'); }
  }

  if (canNavigate(new URL(location.href))) {
    (isCalendar(new URL(location.href)) ? calendarPages : pages).set(
      pageKey(new URL(location.href)), { html: document.documentElement.outerHTML, time: Date.now() });
  }
  scheduleCalendarNeighbors();
  if ('requestIdleCallback' in window) requestIdleCallback(prefetch, { timeout: 1800 });
  else setTimeout(prefetch, 800);

  // Native Android and Windows navigation can use the same cache-first path.
  window.awesomeInterviewNavigate = (url) => {
    const target = new URL(url, location.href);
    if (target.origin !== location.origin || !canNavigate(target) ||
        document.querySelector('[data-experience-editor]')) return false;
    if (pageKey(target) !== pageKey(new URL(location.href)) || !pageEdited)
      navigate(target.href, pageKey(target) !== pageKey(new URL(location.href)));
    else load(pageKey(target), true).catch(() => {});
    return true;
  };

  document.addEventListener('pointerover', event => {
    const link = event.target.closest('.page-nav a[href]');
    if (!link || document.hidden || document.querySelector('[data-experience-editor]')) return;
    const target = new URL(link.href, location.href);
    if (target.origin === location.origin && canNavigate(target) && pageKey(target) !== pageKey(new URL(location.href)))
      load(pageKey(target)).catch(() => {});
  });
  document.addEventListener('click', (event) => {
    const link = event.target.closest('.page-nav a[href], a.brand[href], .personal-calendar a[href], .personal-view-switch a[href], .personal-form-page a[href], .personal-side-list a[href]');
    if (!link || event.defaultPrevented || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey ||
        link.target || document.querySelector('[data-experience-editor]')) return;
    const target = new URL(link.href, location.href);
    if (target.origin !== location.origin || !canNavigate(target)) return;
    event.preventDefault();
    if (pageKey(target) === pageKey(new URL(location.href))) return;
    navigate(target.href);
  });
  window.addEventListener('popstate', () => {
    const editor = document.querySelector('[data-experience-editor]');
    if (editor) {
      const base = `/my-schedule/experiences/${editor.dataset.experienceId}`;
      if ([base, `${base}/write`, `${base}/import`].includes(location.pathname)) return;
    }
    if (canNavigate(new URL(location.href))) navigate(location.href, false);
    else location.reload();
  });
  document.addEventListener('input', (event) => {
    if (event.target.closest('.page > main')) pageEdited = true;
  });
  document.addEventListener('change', (event) => {
    if (event.target.closest('.page > main')) pageEdited = true;
  });
  document.addEventListener('submit', (event) => {
    if (event.target.method?.toUpperCase() === 'POST') {
      clearPages();
      channel?.postMessage('invalidate');
    }
  });
})();
