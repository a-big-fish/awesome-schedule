(() => {
  document.addEventListener('click', event => {
    const root = event.target.closest('[data-reference-editor]');
    if (!root) return;
    const rows = root.querySelector('[data-reference-rows]');
    const status = root.querySelector('[data-reference-status]');
    const add = () => {
      if (rows.children.length >= 20) { status.textContent = '最多添加 20 条参考资料'; return null; }
      const row = rows.firstElementChild.cloneNode(true);
      row.querySelectorAll('input').forEach(input => { input.value = ''; });
      rows.append(row);
      return row;
    };
    if (event.target.closest('[data-reference-add]')) add()?.querySelector('input').focus();
    if (event.target.closest('[data-reference-remove]')) {
      const row = event.target.closest('[data-reference-row]');
      if (rows.children.length > 1) row.remove();
      else row.querySelectorAll('input').forEach(input => { input.value = ''; });
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
