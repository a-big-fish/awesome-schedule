(() => {
  const HOLD_MS = 800;
  const MOVEMENT_TOLERANCE = 8;
  const states = new WeakMap();
  const overlaps = (a, b) => a !== b && a.start < b.end && b.start < a.end;
  const hoverLeft = slot => slot.hoverLeft ?? slot.left;
  const changedSlot = (source, slot) => slot.priority !== source.priority || slot.order !== source.order;
  const digits = '0123456789abcdefghijklmnopqrstuvwxyz';

  // Existing keys always end with a nonzero digit, so there is room between any two keys.
  function between(lower = '', upper) {
    let prefix = '';
    for (let i = 0; ; i++) {
      const low = i < lower.length ? digits.indexOf(lower[i]) : 0;
      const high = upper === undefined ? digits.length - 1 : digits.indexOf(upper[i]);
      if (high - low > 1) return prefix + digits[Math.floor((low + high) / 2)];
      prefix += digits[low];
      if (high > low) upper = undefined;
    }
  }

  function makeSlots(source, peers, place) {
    const levels = [...new Set(peers.map(item => item.priority))].sort((a, b) => b - a);
    const ranks = new Set([source.priority, Math.min(20, levels[0] + 1), Math.max(1, levels.at(-1) - 1)]);
    levels.forEach((level, index) => {
      ranks.add(level);
      const next = levels[index + 1];
      if (level - next > 1) ranks.add(Math.floor((level + next) / 2));
    });
    const sorted = [...ranks].sort((a, b) =>
      Number(b === source.priority) - Number(a === source.priority)
      || Number(levels.includes(a)) - Number(levels.includes(b))
      || Math.abs(a - source.priority) - Math.abs(b - source.priority));
    const slots = new Map([[source.left, {left: source.left, priority: source.priority, order: source.order}]]);
    for (const priority of sorted) {
      const keys = [...new Set(peers.filter(item => item.priority === priority).map(item => item.order))].sort();
      const options = new Set([source.order]);
      if (keys.length) {
        for (let i = 0; i <= keys.length; i++) options.add(between(keys[i - 1], keys[i]));
      }
      for (const order of options) {
        if (order.length > 256) continue;
        const left = place(priority, order);
        if (!slots.has(left)) slots.set(left, {left, priority, order});
      }
    }
    const positions = [...slots.values()].sort((a, b) => a.left - b.left);
    if (source.priority > 1 && levels.includes(source.priority)) {
      // At the right edge of a tied group, moving after it and dropping one level
      // can have the same layout. Give the lower level a distinct pointer target.
      const priority = source.priority - 1;
      const left = place(priority, source.order);
      const index = positions.findIndex(slot => slot.left === left && slot.priority === source.priority);
      if (index !== -1) {
        const next = positions[index + 1];
        const reach = Math.min(72, Math.max(28, (source.width ?? 80) * .6));
        const distance = next ? Math.min(reach, (next.left - left) / 2) : reach;
        positions.splice(index + 1, 0, {left, priority, order: source.order, hoverLeft: left + distance});
      }
    }
    return positions;
  }

  function nearestSlot(slots, left, currentLeft) {
    return slots.reduce((best, slot) => {
      const distance = Math.abs(hoverLeft(slot) - left);
      const bestDistance = Math.abs(hoverLeft(best) - left);
      return distance < bestDistance || (distance === bestDistance && hoverLeft(slot) === currentLeft) ? slot : best;
    });
  }

  function create(grid) {
    const wrap = grid.closest('.time-grid-wrap');
    const table = grid.closest('.personal-time-table');
    const calendar = table.closest('.personal-calendar');
    const hint = document.createElement('div');
    hint.className = 'calendar-priority-drag-hint';
    const help = document.createElement('span');
    help.textContent = '长按后左右拖动，拖到同分组右侧可降级';
    const status = document.createElement('span');
    status.setAttribute('role', 'status');
    hint.append(help, status);
    table.before(hint);
    let placements = [], eligible = new Map(), slotsFor, projectDrop;
    let gesture, frame, suppressClick, baseMinWidth = null, saving = false;
    const shifted = new Set();

    function releaseRepaint() {
      grid.removeAttribute('data-priority-gesture');
      grid.dispatchEvent(new Event('personal:priority-drag-ended', {bubbles: true}));
    }

    function setShift(card, delta) {
      if (delta) {
        card.style.setProperty('--priority-preview-shift', `${delta}px`);
        shifted.add(card);
      } else if (shifted.delete(card)) {
        card.style.removeProperty('--priority-preview-shift');
      }
    }

    function clearPreview() {
      for (const card of shifted) {
        card.style.removeProperty('--priority-preview-shift');
        card.classList.remove('calendar-priority-drag-landed');
      }
      shifted.clear();
      if (baseMinWidth !== null) {
        grid.style.minWidth = baseMinWidth;
        baseMinWidth = null;
      }
    }

    function preview(drag, slot) {
      if (drag.previewSlot === slot) return;
      drag.previewSlot = slot;
      const baseline = placements;
      const target = slot && changedSlot(drag.source, slot)
        ? projectDrop(drag.source, slot.priority, slot.order) : baseline;
      const initialLefts = new Map(baseline.map(item => [item.card, item.left]));
      let requiredWidth = 0;
      for (const {card, left, width} of target) {
        if (card !== drag.source.card) setShift(card, left - initialLefts.get(card));
        requiredWidth = Math.max(requiredWidth, left + width + 10);
      }
      if (requiredWidth > grid.clientWidth) grid.style.minWidth = `${requiredWidth}px`;
    }

    function finish(commit = false) {
      const original = gesture;
      if (!original) return;
      gesture = null;
      clearTimeout(original.timer);
      cancelAnimationFrame(frame);
      frame = 0;
      const {card, left} = original.source;
      if (commit) {
        if (original.slot.left !== left) {
          card.classList.add('calendar-priority-drag-landed');
          setShift(card, original.slot.left - left);
        }
      } else {
        clearPreview();
      }
      card.classList.remove('calendar-priority-dragging');
      original.ghost?.remove();
      original.guide?.remove();
      if (original.source.card.hasPointerCapture(original.pointerId)) {
        original.source.card.releasePointerCapture(original.pointerId);
      }
      if (!commit) releaseRepaint();
    }

    function scheduleDrag() {
      if (!frame) frame = requestAnimationFrame(() => updateDrag());
    }

    function updateDrag(scroll = true) {
      frame = 0;
      const drag = gesture;
      if (!drag?.ghost || !grid.isConnected) { finish(); return; }
      const viewport = wrap.getBoundingClientRect();
      const inside = drag.clientY >= viewport.top && drag.clientY <= viewport.bottom
        && drag.clientX >= viewport.left && drag.clientX <= viewport.right;
      const previousScroll = wrap.scrollLeft;
      if (scroll && inside && drag.moved) {
        const edge = 28;
        if (drag.clientX < viewport.left + edge) wrap.scrollLeft -= 12;
        else if (drag.clientX > viewport.right - edge) wrap.scrollLeft += 12;
      }
      const gridRect = grid.getBoundingClientRect();
      const x = drag.clientX - gridRect.left - drag.grabX;
      drag.slot = nearestSlot(drag.slots, x, drag.source.left);
      preview(drag, inside ? drag.slot : null);
      const visibleLeft = Math.max(0, Math.min(x, grid.clientWidth - drag.source.width));
      drag.ghost.style.transform = `translateX(${visibleLeft - drag.source.left}px)`;
      drag.ghost.style.setProperty('--event-priority', drag.slot.priority);
      drag.ghost.style.setProperty('--drag-score-offset',
        `${Math.max(0, viewport.top - gridRect.top - drag.source.card.offsetTop)}px`);
      const score = `优先级 ${drag.slot.priority}`;
      drag.ghost.querySelector('.calendar-priority-drag-score').textContent = score;
      const priorityLine = drag.ghost.querySelector('.event-priority-line');
      if (priorityLine) priorityLine.textContent = score;
      drag.guide.style.left = `${drag.slot.left}px`;
      drag.guide.hidden = !inside;
      drag.inside = inside;
      if (wrap.scrollLeft !== previousScroll) scheduleDrag();
    }

    function begin() {
      const drag = gesture;
      if (!drag || !grid.isConnected) { finish(); return; }
      const card = drag.source.card;
      drag.slots = slotsFor(drag.source);
      try { card.setPointerCapture(drag.pointerId); }
      catch { finish(); return; }
      baseMinWidth = grid.style.minWidth;
      const dropWidth = Math.max(...drag.slots.map(slot => hoverLeft(slot))) + drag.source.width + 10;
      if (dropWidth > grid.clientWidth) grid.style.minWidth = `${Math.ceil(dropWidth)}px`;
      const ghost = card.cloneNode(true);
      ghost.removeAttribute('href');
      ghost.removeAttribute('title');
      ghost.removeAttribute('aria-keyshortcuts');
      ghost.setAttribute('aria-hidden', 'true');
      ghost.classList.add('calendar-priority-drag-ghost');
      const score = document.createElement('span');
      score.className = 'calendar-priority-drag-score';
      ghost.append(score);
      const guide = document.createElement('span');
      guide.className = 'calendar-priority-drag-guide';
      guide.setAttribute('aria-hidden', 'true');
      guide.style.top = `${card.offsetTop}px`;
      guide.style.height = `${card.offsetHeight}px`;
      guide.style.width = `${drag.source.width}px`;
      drag.ghost = ghost;
      drag.guide = guide;
      card.classList.add('calendar-priority-dragging');
      grid.append(guide, ghost);
      suppressClick = card;
      updateDrag();
    }

    async function save(source, slot, restoreFocus) {
      if (saving || !slot || !changedSlot(source, slot)) return;
      saving = true;
      const href = source.card.getAttribute('href');
      const eventId = decodeURIComponent(href.split('/').at(-1));
      try {
        await window.awesomeInterviewCalendarData.queuePriority(eventId, slot.priority, slot.order);
        grid.removeAttribute('data-priority-gesture');
        await window.awesomeInterviewCalendarChanged?.();
        const current = document.querySelector('.calendar-priority-drag-hint [role="status"]');
        if (current) current.textContent = `${slot.priority === source.priority ? '已调整同分顺序' : '已保存'} · 优先级 ${slot.priority}`;
        if (restoreFocus) {
          const card = [...document.querySelectorAll('.time-grid .event-card')].find(item => item.getAttribute('href') === href);
          card?.focus({preventScroll: true});
        }
      } catch (error) {
        status.textContent = error.message || '保存优先级失败，请重试。';
      } finally {
        clearPreview();
        if (grid.hasAttribute('data-priority-gesture')) releaseRepaint();
        saving = false;
      }
    }

    grid.addEventListener('pointerdown', event => {
      if (!event.isPrimary || event.button !== 0) return;
      suppressClick = null;
      const source = eligible.get(event.target.closest('.event-card'));
      if (!source || saving || gesture) return;
      gesture = {
        source, pointerId: event.pointerId, startX: event.clientX, startY: event.clientY,
        clientX: event.clientX, clientY: event.clientY,
        grabX: event.clientX - source.card.getBoundingClientRect().left,
        timer: setTimeout(begin, HOLD_MS),
      };
      grid.setAttribute('data-priority-gesture', '');
    });
    grid.addEventListener('pointermove', event => {
      const drag = gesture;
      if (!drag || event.pointerId !== drag.pointerId) return;
      drag.clientX = event.clientX;
      drag.clientY = event.clientY;
      const moved = Math.hypot(drag.clientX - drag.startX, drag.clientY - drag.startY) > MOVEMENT_TOLERANCE;
      if (!drag.ghost && moved) { finish(); return; }
      drag.moved ||= moved;
      if (drag.ghost) scheduleDrag();
    });
    wrap.addEventListener('scroll', () => { if (gesture?.ghost) scheduleDrag(); }, {passive: true});
    grid.addEventListener('pointerleave', () => { if (!gesture?.ghost) finish(); });
    grid.addEventListener('pointerup', event => {
      const drag = gesture;
      if (!drag || event.pointerId !== drag.pointerId) return;
      if (drag.ghost) {
        drag.clientX = event.clientX;
        drag.clientY = event.clientY;
        cancelAnimationFrame(frame);
        updateDrag(false);
      }
      const commit = drag.moved && drag.inside && changedSlot(drag.source, drag.slot);
      finish(commit);
      if (commit) void save(drag.source, drag.slot, false);
    });
    grid.addEventListener('pointercancel', event => {
      if (gesture?.pointerId === event.pointerId) finish();
    });
    grid.addEventListener('lostpointercapture', event => {
      if (gesture?.pointerId === event.pointerId && event.target === gesture.source.card) finish();
    });
    grid.addEventListener('contextmenu', event => {
      if (gesture?.source.card.contains(event.target)) event.preventDefault();
    });
    grid.addEventListener('click', event => {
      if (event.detail && suppressClick && suppressClick.contains(event.target)) {
        event.preventDefault();
        event.stopPropagation();
        suppressClick = null;
      }
    }, true);
    grid.addEventListener('keydown', event => {
      if (event.key === 'Escape' && gesture) { finish(); event.preventDefault(); return; }
      const source = eligible.get(event.target);
      if (!source || saving || gesture || !['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
      const slots = slotsFor(source);
      const index = slots.findIndex(slot => !changedSlot(source, slot));
      event.preventDefault();
      void save(source, slots[index + (event.key === 'ArrowLeft' ? -1 : 1)], true);
    });

    return (cards, placed, pack) => {
      finish();
      for (const {card} of placements) {
        card.classList.remove('calendar-priority-draggable');
        card.removeAttribute('draggable');
        card.removeAttribute('aria-keyshortcuts');
      }
      placements = placed;
      const active = placed.filter(({card}) => !card.matches('.is-completed, .is-cancelled'));
      eligible = new Map(active.filter(item => active.some(peer => overlaps(item, peer)))
        .map(item => [item.card, item]));
      hint.hidden = !eligible.size;
      calendar.classList.toggle('has-priority-drag', eligible.size > 0);
      for (const card of eligible.keys()) {
        card.classList.add('calendar-priority-draggable');
        card.draggable = false;
        card.setAttribute('aria-keyshortcuts', 'ArrowLeft ArrowRight');
      }
      const sizes = new Map(placed.map(item => [item.card, item]));
      projectDrop = (source, priority, order) =>
        pack(cards, card => sizes.get(card).width, card => card === source.card ? priority : sizes.get(card).priority,
          card => card === source.card ? order : sizes.get(card).order);
      slotsFor = source => makeSlots(source, active.filter(peer => overlaps(source, peer)), (priority, order) =>
        projectDrop(source, priority, order).find(item => item.card === source.card).left);
    };
  }

  function update(grid, cards, placed, pack) {
    if (!window.awesomeInterviewCalendarData?.queuePriority || !grid.closest('.personal-time-table')) return;
    if (!states.has(grid)) states.set(grid, create(grid));
    states.get(grid)(cards, placed, pack);
  }

  if (typeof module !== 'undefined' && module.exports) module.exports = {between, makeSlots, nearestSlot};
  else window.awesomeInterviewPriorityDrag = {update};
})();
