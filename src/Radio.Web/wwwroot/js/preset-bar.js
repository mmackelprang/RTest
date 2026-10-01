// UI-20 — the radio panel's preset bar (spec 2026-10-01 §4.5).
//
// This module measures and scrolls; it makes no decisions. Which card to reveal, whether an arrow is
// disabled, where the "your station is this way" dot goes and whether a manual scroll is still
// holding off auto-scroll are all decided in PresetBar.razor, where they are unit-tested. This side
// reports what is actually on screen (the fully visible item range and whether the strip is at
// either end) and moves the strip when asked.
//
// Items are the viewport's children carrying data-item="<index>" — preset cards and the ＋ SAVE
// placeholder alike. Every scroll target is an item's left edge, because the viewport snaps
// (scroll-snap-type: x mandatory, scroll-snap-align: start): a target between snap points would be
// moved by the browser to a neighbour, and could cut off the very card it was meant to show.

const bars = new WeakMap();

function items(viewport) {
  return Array.from(viewport.querySelectorAll(':scope > [data-item]'));
}

// Left / right of an item in scroll-content coordinates (what scrollLeft is measured in).
function extent(viewport, el) {
  const vr = viewport.getBoundingClientRect();
  const r = el.getBoundingClientRect();
  const left = viewport.scrollLeft + (r.left - vr.left);
  return { left, right: left + r.width };
}

function behaviour(smooth) {
  const reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  return smooth && !reduce ? 'smooth' : 'auto';
}

function measure(viewport) {
  const list = items(viewport);
  const viewLeft = viewport.scrollLeft;
  const viewRight = viewLeft + viewport.clientWidth;
  let first = -1;
  let last = -1;
  for (const el of list) {
    const { left, right } = extent(viewport, el);
    // 1 px tolerance: sub-pixel layout must not make a fully visible card read as clipped.
    if (left >= viewLeft - 1 && right <= viewRight + 1) {
      const idx = Number(el.dataset.item);
      if (first < 0 || idx < first) first = idx;
      if (idx > last) last = idx;
    }
  }
  const maxScroll = viewport.scrollWidth - viewport.clientWidth;
  return {
    first,
    last,
    atStart: viewLeft <= 1,
    atEnd: viewLeft >= maxScroll - 1,
    maxScroll,
  };
}

function report(viewport) {
  const s = bars.get(viewport);
  if (!s) return;
  const m = measure(viewport);

  // Position track (spec §4.5 "2 px position track"): written straight to CSS custom properties —
  // a round trip through the server for a cosmetic thumb would lag the finger.
  const total = viewport.scrollWidth || 1;
  s.bar.style.setProperty('--rcp-bar-thumb-left', `${(viewport.scrollLeft / total) * 100}%`);
  s.bar.style.setProperty('--rcp-bar-thumb-width', `${Math.min(100, (viewport.clientWidth / total) * 100)}%`);
  s.bar.classList.toggle('is-scrollable', m.maxScroll > 1);

  const key = `${m.first}|${m.last}|${m.atStart}|${m.atEnd}`;
  if (key === s.lastKey) return;
  s.lastKey = key;
  s.dotnet.invokeMethodAsync('OnViewportChanged', m.first, m.last, m.atStart, m.atEnd).catch(() => {});
}

function scheduleReport(viewport) {
  const s = bars.get(viewport);
  if (!s || s.raf) return;
  s.raf = requestAnimationFrame(() => {
    s.raf = 0;
    report(viewport);
  });
}

// Marks a scroll as ours until it ends. scrollTo to the current position fires neither scroll nor
// scrollend, so a timer clears the mark too; otherwise it would swallow the user's next swipe.
function beginProgrammatic(s) {
  endProgrammatic(s);
  s.programmatic = setTimeout(() => { s.programmatic = 0; }, 1500);
}

function endProgrammatic(s) {
  if (s.programmatic) clearTimeout(s.programmatic);
  s.programmatic = 0;
}

export function init(viewport, bar, dotnet) {
  if (!viewport || bars.has(viewport)) return;
  const s = { bar, dotnet, lastKey: '', raf: 0, programmatic: 0, notified: false, down: null, moved: false };
  bars.set(viewport, s);

  // A scroll the user drives (finger, wheel) holds off auto-scroll for 10 s; a scroll this module
  // starts (page / reveal) must not. So the module marks its own scrolls rather than trying to
  // recognise the user's: anything that scrolls while no programmatic scroll is in flight is manual.
  // That also counts the browser re-clamping scrollLeft after a card is deleted, which only means a
  // harmless 10 s hold right after the user touched the bar anyway.
  s.onScroll = () => {
    scheduleReport(viewport);
    if (!s.programmatic && !s.notified) {
      s.notified = true;
      dotnet.invokeMethodAsync('OnManualScroll').catch(() => {});
    }
  };
  s.onScrollEnd = () => {
    report(viewport);
    endProgrammatic(s);
    s.notified = false;
  };

  // Spec §4.5: more than 10 px of movement cancels the tap and the long-press. A touch pan already
  // fires pointercancel natively; this covers the movement the browser does not turn into a pan.
  s.onPointerDown = (e) => {
    s.down = { x: e.clientX, y: e.clientY, target: e.target };
    s.moved = false;
  };
  s.onPointerMove = (e) => {
    if (!s.down || s.moved) return;
    if (Math.hypot(e.clientX - s.down.x, e.clientY - s.down.y) > 10) {
      s.moved = true;
      // Cancels PresetCard's long-press timer through its existing pointercancel handler.
      s.down.target.dispatchEvent(new PointerEvent('pointercancel', { bubbles: true }));
    }
  };
  s.onPointerUp = () => {
    s.down = null;
  };
  // Capture phase on the viewport runs before Blazor's document-level click delegation, so a click
  // ending a >10 px drag never reaches a card's recall handler.
  s.onClickCapture = (e) => {
    if (s.moved) {
      e.stopPropagation();
      e.preventDefault();
      s.moved = false;
    }
  };
  // Spec §8: Left / Right move focus between cards.
  s.onKeyDown = (e) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    // Items are role="listitem" wrappers; the focusable thing is the card or button inside.
    const list = items(viewport)
      .map((el) => el.querySelector('[tabindex="0"], button'))
      .filter(Boolean);
    const i = list.indexOf(document.activeElement);
    if (i < 0) return;
    const next = list[e.key === 'ArrowRight' ? Math.min(list.length - 1, i + 1) : Math.max(0, i - 1)];
    if (next) {
      e.preventDefault();
      next.focus();
    }
  };

  viewport.addEventListener('scroll', s.onScroll, { passive: true });
  viewport.addEventListener('scrollend', s.onScrollEnd);
  viewport.addEventListener('pointerdown', s.onPointerDown);
  viewport.addEventListener('pointermove', s.onPointerMove);
  viewport.addEventListener('pointerup', s.onPointerUp);
  viewport.addEventListener('pointercancel', s.onPointerUp);
  viewport.addEventListener('click', s.onClickCapture, true);
  viewport.addEventListener('keydown', s.onKeyDown);

  // Cards arriving or leaving (save, delete, band change) and the panel resizing both change what is
  // visible without a scroll event.
  s.resize = new ResizeObserver(() => scheduleReport(viewport));
  s.resize.observe(viewport);
  s.mutation = new MutationObserver(() => scheduleReport(viewport));
  s.mutation.observe(viewport, { childList: true });

  report(viewport);
}

// One page in the given direction (-1 or +1): the viewport's width, landing on a card's left edge.
export function page(viewport, direction) {
  const list = items(viewport);
  if (!list.length) return;
  const viewLeft = viewport.scrollLeft;
  const width = viewport.clientWidth;
  const lefts = list.map((el) => extent(viewport, el).left);
  let target;
  if (direction > 0) {
    // The first card not fully visible on the right becomes the first card of the next page.
    const rights = list.map((el) => extent(viewport, el).right);
    const i = rights.findIndex((r) => r > viewLeft + width + 1);
    target = i < 0 ? viewport.scrollWidth : lefts[i];
  } else {
    // The earliest card edge that keeps the current first card within one viewport width back.
    const goal = viewLeft - width;
    const i = lefts.findIndex((l) => l >= goal - 1);
    target = i < 0 ? 0 : lefts[i];
  }
  beginProgrammatic(bars.get(viewport) || {});
  viewport.scrollTo({ left: Math.max(0, target), behavior: behaviour(true) });
}

// Scroll the least distance that shows item `index` fully, landing on a card edge. No-op when the
// item is already fully visible or absent.
export function reveal(viewport, index, smooth) {
  const list = items(viewport);
  const el = list.find((x) => Number(x.dataset.item) === index);
  if (!el) return;
  const { left, right } = extent(viewport, el);
  const viewLeft = viewport.scrollLeft;
  const width = viewport.clientWidth;
  let target = null;
  if (left < viewLeft - 1) {
    target = left;
  } else if (right > viewLeft + width + 1) {
    // Smallest snap point (a card's left edge) that still has the target's right edge in view.
    const lefts = list.map((x) => extent(viewport, x).left);
    target = lefts.find((l) => right - l <= width + 1) ?? left;
  }
  if (target !== null) {
    beginProgrammatic(bars.get(viewport) || {});
    viewport.scrollTo({ left: Math.max(0, target), behavior: behaviour(smooth) });
  }
}

export function dispose(viewport) {
  const s = viewport && bars.get(viewport);
  if (!s) return;
  viewport.removeEventListener('scroll', s.onScroll);
  viewport.removeEventListener('scrollend', s.onScrollEnd);
  viewport.removeEventListener('pointerdown', s.onPointerDown);
  viewport.removeEventListener('pointermove', s.onPointerMove);
  viewport.removeEventListener('pointerup', s.onPointerUp);
  viewport.removeEventListener('pointercancel', s.onPointerUp);
  viewport.removeEventListener('click', s.onClickCapture, true);
  viewport.removeEventListener('keydown', s.onKeyDown);
  s.resize.disconnect();
  s.mutation.disconnect();
  if (s.raf) cancelAnimationFrame(s.raf);
  endProgrammatic(s);
  bars.delete(viewport);
}
