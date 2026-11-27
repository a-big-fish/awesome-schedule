(() => {
  const notice = document.querySelector('[data-database-status]');
  const fallback = document.querySelector('[data-database-fallback]');
  const key = document.querySelector('meta[name="calendar-session-key"]')?.content;
  const csrf = document.querySelector('meta[name="calendar-csrf"]');
  const storageKey = 'awesome-interview-calendar-session-v1';
  let identity = document.querySelector('meta[name="calendar-user"]');
  let pending = null, retryAt = 0, expired = false, checkTimer = null;
  let unavailable = !!fallback;

  try {
    if (fallback && !identity && key) {
      const saved = JSON.parse(localStorage.getItem(storageKey));
      if (saved?.key === key && typeof saved.scope === 'string' && saved.scope) {
        identity = document.createElement('meta');
        identity.name = 'calendar-user';
        identity.content = saved.scope;
        document.head.append(identity);
      }
    } else if (!fallback && identity && key) {
      localStorage.setItem(storageKey, JSON.stringify({key, scope: identity.content}));
    }
  } catch { /* Storage may be disabled. */ }

  function clearIdentity() {
    expired = true;
    clearTimeout(checkTimer);
    try {
      if (JSON.parse(localStorage.getItem(storageKey))?.key === key) localStorage.removeItem(storageKey);
    } catch { /* Storage may be disabled. */ }
  }
  document.addEventListener('awesome-interview:session-expired', () => {
    clearIdentity();
    if (fallback) location.assign('/auth/login');
  });
  document.addEventListener('submit', event => {
    if (!event.defaultPrevented && event.target.matches('form[action="/auth/logout"]')) clearIdentity();
  });

  function report(response, data) {
    if (!notice) return;
    if (response.ok && unavailable) {
      unavailable = false;
      notice.textContent = '数据库连接已恢复。';
      return;
    }
    const failure = response.headers?.get('X-Database-Unavailable');
    if (!failure) return;
    unavailable = true;
    const label = failure === 'timeout' || data?.detail?.startsWith('数据库连接超时')
      ? '数据库连接超时' : '数据库连接暂时不可用';
    notice.textContent = label + '。已缓存的数据仍可使用，修改会在恢复后同步。';
    notice.hidden = false;
  }

  async function session() {
    if (expired) return {status: 401};
    if (!fallback || !key || csrf?.content) return {token: csrf?.content || ''};
    if (pending) return pending;
    if (!identity || Date.now() < retryAt) return {status: 503};
    pending = (async () => {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 12000);
      try {
        const response = await fetch('/api/mobile/v1/session', {
          credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
          headers: {Accept: 'application/json'},
        });
        const data = await response.json().catch(() => null);
        if (response.status === 401 || response.status === 403 ||
            (response.ok && data?.user_id !== identity.content)) {
          document.dispatchEvent(new Event('awesome-interview:session-expired'));
          notice.textContent = '登录已过期。请重新登录后继续同步。';
          return {status: 401};
        }
        if (!response.ok || !data?.csrf_token) {
          retryAt = Date.now() + 30000;
          report(response, data);
          return {status: 503};
        }
        csrf.content = data.csrf_token;
        unavailable = false;
        notice.textContent = '数据库连接已恢复，待同步的修改将继续提交。';
        document.dispatchEvent(new Event('personal:database-restored'));
        return {token: csrf.content};
      } catch {
        retryAt = Date.now() + 30000;
        return {status: 503};
      } finally { clearTimeout(timeout); pending = null; }
    })();
    return pending;
  }

  function retry() {
    if (expired) return;
    if (document.hidden || navigator.onLine === false) {
      checkTimer = setTimeout(retry, 30000);
      return;
    }
    void session().then(result => {
      if (result.status === 503 && !expired) checkTimer = setTimeout(retry, 30000);
    });
  }
  if (fallback && key && identity) checkTimer = setTimeout(retry, 30000);
  window.awesomeInterviewDatabaseStatus = {fallback: !!fallback, session, report, isUnavailable: () => unavailable};
})();
