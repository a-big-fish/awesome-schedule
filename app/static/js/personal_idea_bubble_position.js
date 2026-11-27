(() => {
  const bubble = document.querySelector('[data-idea-bubble]');
  if (!bubble) return;
  const handle = bubble.querySelector('summary');
  const panel = bubble.querySelector('.idea-bubble-panel');
  const key = 'awesome-interview-idea-bubble-position-v1';
  let position = null, drag = null, suppressClick = false;
  try {
    const saved = JSON.parse(localStorage.getItem(key));
    if (saved?.version === 1 && [saved.x, saved.y].every(n => Number.isFinite(n) && n >= 0 && n <= 1)) position = saved;
  } catch { /* Dragging still works when browser storage is unavailable. */ }
  const clamp = (n, max) => Math.max(12, Math.min(n, Math.max(12, max)));
  const limits = () => ({
    x: Math.max(12, window.innerWidth - bubble.offsetWidth - 12),
    y: Math.max(12, window.innerHeight - handle.offsetHeight - (window.innerWidth <= 640 ? 84 : 12)),
  });
  function placePanel() {
    if (!bubble.open || bubble.hidden) return;
    const rect = handle.getBoundingClientRect();
    const above = rect.top - 24, below = window.innerHeight - rect.bottom - 24;
    panel.style.maxHeight = `${Math.max(80, Math.max(above, below))}px`;
    const height = panel.getBoundingClientRect().height;
    panel.style.position = 'fixed'; panel.style.bottom = 'auto'; panel.style.right = 'auto';
    panel.style.left = `${clamp(rect.right - panel.offsetWidth, window.innerWidth - panel.offsetWidth - 12)}px`;
    panel.style.top = `${clamp(above >= below ? rect.top - height - 12 : rect.bottom + 12,
      window.innerHeight - height - 12)}px`;
  }
  function move(x, y, save = false) {
    const max = limits();
    x = clamp(x, max.x); y = clamp(y, max.y);
    Object.assign(bubble.style, {left: `${x}px`, top: `${y}px`, right: 'auto', bottom: 'auto'});
    position = {version: 1, x: (x - 12) / Math.max(1, max.x - 12), y: (y - 12) / Math.max(1, max.y - 12)};
    if (save) try { localStorage.setItem(key, JSON.stringify(position)); } catch { /* Best effort. */ }
    placePanel();
  }
  function restore() {
    if (bubble.hidden) return;
    if (position) { const max = limits(); move(12 + position.x * (max.x - 12), 12 + position.y * (max.y - 12)); }
    placePanel();
  }
  handle.title = '拖动调整位置；也可使用 Alt + 方向键移动';
  handle.addEventListener('pointerdown', event => {
    if (event.button !== 0 || event.isPrimary === false) return;
    const rect = handle.getBoundingClientRect();
    suppressClick = false;
    drag = {id: event.pointerId, x: event.clientX, y: event.clientY, left: rect.left, top: rect.top, moved: false};
    handle.setPointerCapture(event.pointerId);
  });
  handle.addEventListener('pointermove', event => {
    if (!drag || event.pointerId !== drag.id) return;
    const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
    if (!drag.moved && Math.hypot(dx, dy) < 6) return;
    drag.moved = true; bubble.open = false;
    move(drag.left + dx, drag.top + dy);
  });
  const end = event => {
    if (!drag || event.pointerId !== drag.id) return;
    suppressClick = drag.moved && event.type !== 'pointercancel';
    if (drag.moved) { const rect = handle.getBoundingClientRect(); move(rect.left, rect.top, true); }
    drag = null;
    if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId);
  };
  handle.addEventListener('pointerup', end);
  handle.addEventListener('pointercancel', end);
  handle.addEventListener('click', event => {
    if (suppressClick) { event.preventDefault(); event.stopPropagation(); suppressClick = false; }
  }, true);
  handle.addEventListener('keydown', event => {
    const delta = {ArrowLeft: [-20, 0], ArrowRight: [20, 0], ArrowUp: [0, -20], ArrowDown: [0, 20]}[event.key];
    if (!event.altKey || !delta) return;
    event.preventDefault(); const rect = handle.getBoundingClientRect(); move(rect.left + delta[0], rect.top + delta[1], true);
  });
  bubble.addEventListener('toggle', placePanel);
  new MutationObserver(restore).observe(bubble, {attributes: true, attributeFilter: ['hidden']});
  window.addEventListener('resize', restore);
  restore();
})();
