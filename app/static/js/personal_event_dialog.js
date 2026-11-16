(() => {
  const dialog = document.querySelector('[data-event-dialog]');
  if (!dialog) return; // Guests retain the existing login gate.
  const content = dialog.querySelector('[data-event-dialog-content]');
  const status = dialog.querySelector('[data-event-dialog-status]');
  let activeUrl = '', generation = 0, controller = null, opener = null, dirty = false, busy = false;
  const eventPath = path => /^\/my-schedule\/events\/[^/]+(?:\/edit)?$/.test(path);
  function message(text, error = false) { status.textContent = text; status.setAttribute('role', error ? 'alert' : 'status'); }
  function close() {
    if (busy) { message('正在保存，请稍候。'); return; }
    if (dirty && !window.confirm('放弃尚未保存的修改吗？')) return;
    generation++; controller?.abort(); dialog.close(); content.replaceChildren(); dirty = false;
    if (opener?.isConnected) opener.focus();
  }
  function paint(html) {
    const parsed = new DOMParser().parseFromString(html, 'text/html');
    const section = parsed.querySelector('.personal-form-page');
    if (!section) throw new Error('无法载入日程，请稍后重试。');
    content.replaceChildren(document.importNode(section, true));
    // Never inject a whole document or execute scripts from a fetched page.
    content.querySelectorAll('script').forEach(script => script.remove());
    dialog.scrollTop = 0; dirty = false;
    document.dispatchEvent(new CustomEvent('personal:event-dialog-rendered'));
    window.awesomeInterviewCalendarData?.rememberForm(content.querySelector('.personal-event-form'));
    content.querySelector('h1')?.setAttribute('tabindex', '-1');
    content.querySelector('h1')?.focus({preventScroll: true});
  }
  async function open(url, source) {
    const target = new URL(url, location.href);
    if (target.origin !== location.origin || !eventPath(target.pathname)) return;
    if (dirty && !window.confirm('放弃尚未保存的修改吗？')) return;
    const version = ++generation;
    controller?.abort(); controller = new AbortController();
    activeUrl = target.pathname;
    if (source && !dialog.contains(source)) opener = source;
    if (!dialog.open) dialog.showModal();
    message('正在加载日程…'); content.setAttribute('aria-busy', 'true');
    try {
      const local = await window.awesomeInterviewCalendarData?.eventPage(activeUrl);
      if (version !== generation) return;
      if (local) { paint(local); message(''); return; }
      const response = await fetch(activeUrl, {credentials: 'same-origin', cache: 'no-store',
        signal: controller.signal, headers: {Accept: 'text/html'}});
      if (version !== generation) return;
      if (response.status === 401 || response.status === 403 || new URL(response.url).pathname.startsWith('/auth/')) {
        dirty = false; close(); document.dispatchEvent(new Event('awesome-interview:session-expired')); return;
      }
      if (!response.ok) throw new Error(response.status === 404 ? '该日程已不存在。' : '暂时无法加载，当前日程表已保留。');
      const html = await response.text(); if (version !== generation) return;
      paint(html); message('');
    } catch (error) { if (error.name !== 'AbortError' && version === generation) message(error.message, true); }
    finally { if (version === generation) content.removeAttribute('aria-busy'); }
  }
  window.awesomeInterviewOpenEvent = url => open(url);
  document.addEventListener('click', event => {
    const link = event.target.closest('a[href]');
    if (!link || event.defaultPrevented || event.button > 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || link.target || link.hasAttribute('download')) return;
    const url = new URL(link.href, location.href);
    if (url.origin !== location.origin) return;
    const inside = dialog.contains(link);
    if (inside && url.pathname === '/my-schedule') { event.preventDefault(); close(); return; }
    if (!eventPath(url.pathname) || (!inside && !link.closest('.personal-calendar, .personal-upcoming'))) return;
    event.preventDefault(); open(url.href, link);
  });
  dialog.querySelector('[data-event-dialog-close]').addEventListener('click', close);
  dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
  dialog.addEventListener('input', () => { dirty = true; });
  dialog.addEventListener('change', () => { dirty = true; });
  dialog.addEventListener('submit', async event => {
    const form = event.target;
    if (event.defaultPrevented) {
      // Another validator refused the submit; without this the click looked like nothing happened.
      const reason = form.querySelector('[data-period-error], [data-time-error], [data-calendar-save-error], .parse-tip.error')
        ?.textContent?.trim();
      message(reason || '请先修正表单中标红的内容。', true);
      return;
    }
    if (form.method.toUpperCase() !== 'POST') return;
    event.preventDefault(); event.stopPropagation();
    if (busy) return;
    busy = true; message('正在保存…'); content.setAttribute('aria-busy', 'true');
    try {
      if (window.awesomeInterviewCalendarData?.queueForm) {
        const result = await window.awesomeInterviewCalendarData.queueForm(form);
        if (!result) { message(''); return; }
        dirty = false; busy = false; close();
        await window.awesomeInterviewCalendarData.showQueued(result); return;
      }
      const response = await fetch(form.action, {method: 'POST', credentials: 'same-origin', cache: 'no-store',
        headers: {Accept: 'text/html', 'X-Event-Dialog': '1', ...(window.awesomeInterviewCalendarData ? {'X-Calendar-Cache': '1'} : {})}, body: new FormData(form)});
      const resolved = new URL(response.url, location.href);
      if (response.status === 401 || response.status === 403 || resolved.pathname.startsWith('/auth/')) {
        message('登录状态已过期，请重新登录后再保存。', true); return;
      }
      if (response.status === 409 || response.status === 422) {
        paint(await response.text()); dirty = true;
        message(response.status === 409 ? '存在重叠安排，请确认是否仍要保存。' : '请检查并修改表单中的错误。', true); return;
      }
      if (!response.ok) throw new Error('保存失败，已保留输入内容，请稍后重试。');
      if (response.headers.get('content-type')?.includes('json')) {
        const result = await response.json();
        if (window.awesomeInterviewCalendarData) await window.awesomeInterviewCalendarData.applyMutation(result);
      }
      dirty = false; busy = false; close();
      if (window.awesomeInterviewCalendarChanged) await window.awesomeInterviewCalendarChanged();
      else location.reload();
    } catch (error) { message(error.message || '网络异常，已保留输入内容。', true); }
    finally { busy = false; content.removeAttribute('aria-busy'); }
  });
  document.addEventListener('personal:navigated', () => { if (dialog.open) { dirty = false; close(); } });
})();
