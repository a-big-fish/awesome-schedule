(() => {
  if (document.querySelector('meta[name="guest-browsing"]')) return;
  // Private HTML stays in this page's memory; the server sends no-store.
  const paths = ['/my-schedule/new', '/my-schedule', '/my-schedule/experiences/plaza', '/my-schedule/experiences',
    '/my-schedule/profile', '/my-schedule/interviews', '/my-schedule/ideas'];
  const allowed = new Set(paths);
  if (!document.querySelector('.personal-app .page-nav a[href="/my-schedule/experiences/plaza"]')) return;
  const ttl = 5 * 60 * 1000;
  const calendarTtl = 18 * 60 * 60 * 1000;
  const pages = new Map();
  const calendarPages = new Map();
  const calendarLimit = 24;
  const pending = new Map();
  const database = window.awesomeInterviewDatabaseStatus;
  let cacheGeneration = 0;
  let navigationId = 0;
  let pageEdited = false;
  let unavailableUntil = 0;
  window.addEventListener('online', () => {unavailableUntil = 0;});
  const unavailable = () => Object.assign(new Error('连接暂时不可用，请稍后重试'), {transient: true});
  const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('awesome-interview-pages') : null;
  const clearPages = () => { cacheGeneration++; pages.clear(); calendarPages.clear(); pending.clear(); };
  channel?.addEventListener('message', clearPages);
  const isCalendar = (target) => target.pathname === '/my-schedule' &&
    [...target.searchParams.keys()].every((key) => ['view', 'date_value', 'show_completed', 'show_all'].includes(key));
  const isEventPage = target => /^\/my-schedule\/events\/[^/]+(?:\/edit)?$/.test(target.pathname) && !target.search;
  const isIdeas = target => (target.pathname === '/my-schedule/ideas' && [...target.searchParams.keys()].every(key=>['edit','abandoned'].includes(key))) || /^\/my-schedule\/ideas\/[^/]+\/arrange$/.test(target.pathname);
  const isExperiences = target => target.pathname === '/my-schedule/experiences' && [...target.searchParams.keys()].every(key=>['q','visibility','order','page'].includes(key));
  const canNavigate = (target) => (allowed.has(target.pathname) && (!target.search || isCalendar(target) || isExperiences(target))) || isIdeas(target) || isEventPage(target);
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
    if (url === '/my-schedule/new' && database?.isUnavailable()) {
      const local = await window.awesomeInterviewCalendarData?.newPage();
      if (local) return local;
    }
    const calendar = isCalendar(new URL(url, location.href));
    if(isIdeas(new URL(url,location.href)) && window.awesomeInterviewIdeaData){
      const local=await window.awesomeInterviewIdeaData.page(url);if(local)return local;
    }
    if (calendar && window.awesomeInterviewCalendarData) {
      const local = await window.awesomeInterviewCalendarData.page(url);
      if (local) return local;
    }
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
        database?.report(response);
        if (url === '/my-schedule/new' && database?.isUnavailable()) {
          const local = await window.awesomeInterviewCalendarData?.newPage();
          if (local) return local;
        }
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
        if(isExperiences(new URL(url,location.href)))void window.awesomeInterviewPrivatePages?.put(url,html);
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
    if (window.awesomeInterviewCalendarData) return;
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
      const targets=['/my-schedule/new','/my-schedule'];
      if(window.awesomeInterviewPrivatePages)targets.push('/my-schedule/experiences');
      for (const path of targets) {
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

  let deferredPriorityRepaint = false;
  function render(html, resetScroll) {
    if (!resetScroll && document.querySelector('[data-priority-gesture]')) {
      deferredPriorityRepaint = true;
      return;
    }
    deferredPriorityRepaint = false;
    const renderStarted = performance.now();
    const page = typeof html === 'string' ? new DOMParser().parseFromString(html, 'text/html') : html;
    const nextMain = page.querySelector('.page > main');
    const currentMain = document.querySelector('.page > main');
    if (!nextMain || !currentMain || !page.body.classList.contains('personal-app'))
      throw new Error('页面结构已变化');
    const scrollSelector = '.time-grid-wrap, .personal-week-timeline, .personal-period-month, .personal-side-list, .personal-event-list';
    const positions = resetScroll ? [] : [...currentMain.querySelectorAll(scrollSelector)]
      .map(element => ({top: element.scrollTop, left: element.scrollLeft}));
    document.title = page.title;
    document.body.className = page.body.className;
    for (const name of ['workday-start', 'workday-end']) {
      const latest = page.querySelector(`meta[name="${name}"]`);
      const current = document.querySelector(`meta[name="${name}"]`);
      if (latest && current) current.content = latest.content;
    }
    currentMain.replaceWith(nextMain);
    nextMain.querySelectorAll('[name="csrf_token"]').forEach(input=>{
      input.value=document.querySelector('meta[name="calendar-csrf"]')?.content||'';
    });
    setNavState(page);
    pageEdited = false;
    if (resetScroll) window.scrollTo(0, 0);
    document.dispatchEvent(new CustomEvent('personal:navigated', {detail: {resetScroll}}));
    if (!resetScroll) [...nextMain.querySelectorAll(scrollSelector)].forEach((element, index) => {
      if (positions[index]) {
        element.scrollTop = positions[index].top;
        element.scrollLeft = positions[index].left;
      }
    });
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
    if(isIdeas(target)&&window.awesomeInterviewIdeaData){
      try{
        const local=await window.awesomeInterviewIdeaData.page(key);
        if(id!==navigationId)return;
        if(local){if(addHistory)history.pushState({personalPage:true},'',key);render(local,true);return;}
      }catch{document.dispatchEvent(new Event('personal:unavailable'));return;}
    }
    if (calendar && window.awesomeInterviewCalendarData) {
      const local = await window.awesomeInterviewCalendarData.page(key);
      if (id !== navigationId) return;
      if (local) {
        if (addHistory) history.pushState({personalPage: true}, '', key);
        render(local, true); return;
      }
    }
    let stored = (calendar ? calendarPages : pages).get(key);
    if(!stored&&isExperiences(target))stored=await window.awesomeInterviewPrivatePages?.get(key);
    if(id!==navigationId)return;
    const cached = stored?.html;
    if (cached) {
      try {
        if (addHistory) history.pushState({ personalPage: true }, '', key);
        render(cached, true);
      } catch {
        location.assign(target.href);
        return;
      }
      if (isFresh(stored, calendar) && !isExperiences(target)) return;
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

  if (!database?.fallback && canNavigate(new URL(location.href))) {
    (isCalendar(new URL(location.href)) ? calendarPages : pages).set(
      pageKey(new URL(location.href)), { html: document.documentElement.outerHTML, time: Date.now() });
    if(isExperiences(new URL(location.href)))void window.awesomeInterviewPrivatePages?.put(pageKey(new URL(location.href)),document.documentElement.outerHTML);
  }
  scheduleCalendarNeighbors();
  if ('requestIdleCallback' in window) requestIdleCallback(prefetch, { timeout: 1800 });
  else setTimeout(prefetch, 800);

  // Keep the already visible snapshot while refreshing; never navigate on resume.
  async function refreshVisiblePage() {
    const target = new URL(location.href);
    if (!canNavigate(target) || document.hidden || pageEdited ||
        document.querySelector('[data-experience-editor], dialog[open]')) return;
    const id = navigationId, generation = cacheGeneration, key = pageKey(target);
    const main = document.querySelector('.page > main');
    const previous = (isCalendar(target) ? calendarPages : pages).get(key)?.html;
    const focused = () => document.activeElement?.matches('input, textarea, select, [contenteditable]');
    if (focused()) return;
    try {
      const html = await load(key, true);
      if (id !== navigationId || generation !== cacheGeneration || pageKey(new URL(location.href)) !== key ||
          main !== document.querySelector('.page > main') || pageEdited || html === previous ||
          document.hidden || focused() || document.querySelector('dialog[open]')) return;
      // Even untouched forms may contain browser autofill or a selected file.
      // Refresh their cached snapshot only, leaving the live form intact.
      if (target.pathname === '/my-schedule/new' || target.pathname === '/my-schedule/profile' ||
          target.pathname.endsWith('/edit')) return;
      render(html, false);
    } catch { /* Keep the visible page on network/DB failure; never reload. */ }
  }

  // A deadline is a data refresh, not a navigation through an old snapshot.
  window.awesomeInterviewRefreshCalendar = () => {
    if (isCalendar(new URL(location.href))) return refreshVisiblePage();
  };

  window.awesomeInterviewCalendarChanged = async () => {
    clearPages();
    // The outbox broadcasts its committed state itself; do not trigger unrelated remote refreshes.
    if (!window.awesomeInterviewCalendarData?.queueForm) channel?.postMessage('invalidate');
    pageEdited = false;
    const target = new URL(location.href), id = navigationId;
    if (!isCalendar(target)) return;
    const html = await load(target.href, true);
    if (id === navigationId && pageKey(new URL(location.href)) === pageKey(target)) render(html, false);
  };

  document.addEventListener('personal:calendar-mutated', clearPages);
  document.addEventListener('personal:priority-drag-ended', () => {
    if (!deferredPriorityRepaint) return;
    deferredPriorityRepaint = false;
    void window.awesomeInterviewCalendarChanged();
  });

  const resumeDelay = 60000;
  let hiddenAt = document.hidden ? Date.now() : null;
  let lastResumeRefresh = -Infinity;
  const resume = () => {
    if (document.hidden || Date.now() - lastResumeRefresh < resumeDelay) return;
    lastResumeRefresh = Date.now();
    refreshVisiblePage();
  };
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) { hiddenAt = Date.now(); return; }
    const away = hiddenAt === null ? 0 : Date.now() - hiddenAt;
    hiddenAt = null;
    if (away >= resumeDelay) resume();
  });
  // Back/forward cache restoration does not necessarily emit visibilitychange.
  window.addEventListener('pageshow', event => { if (event.persisted) resume(); });

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

  if (database?.fallback) {
    const restore = async () => {
      const target = new URL(location.href);
      const calendar = window.awesomeInterviewCalendarData;
      let page;
      if (target.pathname === '/my-schedule/new') page = await calendar?.newPage();
      else if (isIdeas(target)) page = await window.awesomeInterviewIdeaData?.page(target.href);
      if (page) {render(page, false); return;}
      const url = isCalendar(target) ? target.href : '/my-schedule';
      page = await calendar?.page(url);
      if (page) {
        history.replaceState({personalPage:true}, '', url);
        render(page, false);
        if (isEventPage(target)) void window.awesomeInterviewOpenEvent?.(target.href);
      } else {
        document.querySelector('[data-database-cache-status]').textContent = '此会话在当前浏览器没有可用的日程缓存。数据库恢复后可重新连接。';
      }
    };
    void restore().catch(()=>{
      const status = document.querySelector('[data-database-cache-status]');
      if (status) status.textContent = '暂时无法读取浏览器缓存，数据库恢复后可重新连接。';
    });
  }

  document.addEventListener('pointerover', event => {
    const link = event.target.closest('.page-nav a[href]');
    if (!link || document.hidden || document.querySelector('[data-experience-editor]')) return;
    const target = new URL(link.href, location.href);
    if (target.origin === location.origin && canNavigate(target) && pageKey(target) !== pageKey(new URL(location.href)))
      load(pageKey(target)).catch(() => {});
  });
  document.addEventListener('click', (event) => {
    const link = event.target.closest('.page-nav a[href], a.brand[href], .personal-calendar a[href], .personal-quick-calendar a[href], .personal-view-switch a[href], .personal-form-page a[href], .personal-side-list a[href], .personal-side-heading a[href], .idea-bubble a[href], .experiences-page a[href]');
    if (!link || event.defaultPrevented || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey ||
        link.target || link.dataset?.databaseRetry !== undefined || document.querySelector('[data-experience-editor]')) return;
    const target = new URL(link.href, location.href);
    if (target.origin !== location.origin || !canNavigate(target)) return;
    event.preventDefault();
    if (pageKey(target) === pageKey(new URL(location.href))) {
      if (isCalendar(target)) document.dispatchEvent(new Event('personal:focus-now'));
      return;
    }
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
    if(event.target.matches?.('.experiences-filters')){
      event.preventDefault();const url=new URL(event.target.action,location.href);
      url.search=new URLSearchParams(new FormData(event.target)).toString();navigate(url.href);return;
    }
    if (event.target.method?.toUpperCase() === 'POST') {
      clearPages();
      channel?.postMessage('invalidate');
    }
  });
})();
