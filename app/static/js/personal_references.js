(() => {
  const titleJobs = new WeakMap();
  const defaultTitleHint = '名称留空时会尝试解析网页标题，请稍作等候；也可手动填写。';
  const titleStatus = (row, text) => {
    const hint = row?.querySelector('[data-reference-title-status]');
    if (hint) hint.textContent = text;
  };
  async function fillTitle(row) {
    const input = row.querySelector('[name="reference_url"]');
    const name = row.querySelector('[name="reference_title"]');
    const url = input.value.trim();
    if (!url || name.value.trim() || titleJobs.get(row)?.url === url) return;
    try { if (!['http:', 'https:'].includes(new URL(url).protocol)) return; } catch { return; }
    const job = {url};
    titleJobs.set(row, job);
    const current = () => row.isConnected && input.value.trim() === url && titleJobs.get(row) === job;
    titleStatus(row, '正在尝试解析网页标题，请稍作等候；不影响保存，也可手动填写名称。');
    try {
      for (let attempt = 0; attempt < 6; attempt++) {
        if (!current() || name.value.trim()) return;
        const response = await fetch(`/my-schedule/reference-title?url=${encodeURIComponent(url)}`, {
          credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(5000)
        });
        if (!response.ok) { if (current()) titleStatus(row, '暂时无法解析标题，可手动填写名称，不影响保存。'); return; }
        const result = await response.json();
        if (!current() || name.value.trim()) return;
        if (result.title) {
          name.value = result.title;
          name.dispatchEvent(new Event('input', {bubbles: true}));
          titleStatus(row, '已自动填入名称，可继续手动修改。');
          return;
        }
        if (!result.pending) { titleStatus(row, '未获取到网页标题，可手动填写名称，不影响保存。'); return; }
        await new Promise(resolve => setTimeout(resolve, 1500 * (attempt + 1)));
      }
      if (current()) titleStatus(row, '解析等待时间较长，可先保存或手动填写名称。');
    } catch { if (current()) titleStatus(row, '暂时无法解析标题，可手动填写名称，不影响保存。'); }
    finally { if (titleJobs.get(row) === job) titleJobs.delete(row); }
  }
  document.addEventListener('input', event => {
    if (!event.target.matches('[name="reference_url"], [name="reference_title"]')) return;
    const row = event.target.closest('[data-reference-row]');
    titleJobs.delete(row);
    titleStatus(row, row.querySelector('[name="reference_title"]').value.trim()
      ? '已填写名称，将使用此名称。' : defaultTitleHint);
  });
  document.addEventListener('focusout', event => {
    if (event.target.matches('[name="reference_url"]')) fillTitle(event.target.closest('[data-reference-row]'));
  });
  document.addEventListener('paste', event => {
    if (event.target.matches('[name="reference_url"]')) {
      const row = event.target.closest('[data-reference-row]');
      setTimeout(() => fillTitle(row), 0);
    }
  });
  document.addEventListener('click', event => {
    const root = event.target.closest('[data-reference-editor]');
    if (!root) return;
    const rows = root.querySelector('[data-reference-rows]');
    const status = root.querySelector('[data-reference-status]');
    const add = () => {
      if (rows.children.length >= 20) { status.textContent = '最多添加 20 条参考资料'; return null; }
      const row = rows.firstElementChild.cloneNode(true);
      row.querySelectorAll('input').forEach(input => { input.value = ''; });
      titleStatus(row, defaultTitleHint);
      rows.append(row);
      return row;
    };
    if (event.target.closest('[data-reference-add]')) add()?.querySelector('input').focus();
    if (event.target.closest('[data-reference-remove]')) {
      const row = event.target.closest('[data-reference-row]');
      if (rows.children.length > 1) row.remove();
      else {
        titleJobs.delete(row);
        row.querySelectorAll('input').forEach(input => { input.value = ''; });
        titleStatus(row, defaultTitleHint);
      }
    }
    if (event.target.closest('[data-reference-import]')) {
      const input = root.querySelector('[data-reference-bulk]');
      const lines = [...new Set(input.value.split(/\r?\n/).map(line => line.trim()).filter(Boolean))];
      try {
        lines.forEach(line => {
          const url = new URL(line);
          if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) throw new Error();
        });
      } catch { status.textContent = '请每行填写一个有效的 HTTP 或 HTTPS 链接'; return; }
      const filled = [...rows.children].filter(row => [...row.querySelectorAll('input')].some(input => input.value.trim()));
      if (filled.length + lines.length > 20) { status.textContent = '合计最多 20 条，请减少链接后重试'; return; }
      for (const line of lines) {
        const row = [...rows.children].find(row => [...row.querySelectorAll('input')].every(input => !input.value.trim())) || add();
        row.querySelector('[name="reference_url"]').value = line;
        fillTitle(row);
      }
      input.value = '';
      status.textContent = `已分条加入 ${lines.length} 个链接`;
    }
    root.dispatchEvent(new Event('input', {bubbles: true}));
  });
  let controller, timer;
  function initTitles() {
    controller?.abort();
    clearTimeout(timer);
    const list = document.querySelector('[data-reference-titles]');
    if (!list) return;
    controller = new AbortController();
    const signal = controller.signal;
    let attempt = 0;
    async function update() {
      if (document.hidden || signal.aborted || !list.isConnected) return;
      attempt++;
      try {
        const response = await fetch(`/my-schedule/events/${encodeURIComponent(list.dataset.referenceTitles)}/reference-titles`, {signal, credentials: 'same-origin', cache: 'no-store'});
        if (!response.ok) return;
        const result = await response.json();
        if (!list.isConnected || signal.aborted) return;
        for (const item of result.links) {
          const link = list.querySelector(`[data-reference-index="${item.index}"]`);
          if (link && item.title) link.textContent = item.title;
        }
        if (result.links.some(item => item.pending) && attempt < 6)
          timer = setTimeout(update, Math.min(10000, 1500 * 2 ** (attempt - 1)));
      } catch { /* Leave the reference label and domain usable when offline. */ }
    }
    update();
  }
  document.addEventListener('DOMContentLoaded', initTitles);
  document.addEventListener('personal:navigated', initTitles);
  window.addEventListener('pagehide', () => { controller?.abort(); clearTimeout(timer); });
})();
