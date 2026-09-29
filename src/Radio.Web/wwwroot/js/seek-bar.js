// SeekBar gesture capture (AUD-28 / UI-16).
//
// Owner ruling 2026-09-25 (AUD-28): SEEK ON RELEASE, no debounce. This module only reports the
// gesture; it never seeks. It calls three [JSInvokable] methods on the SeekBar component:
//
//   OnDragMove(fraction)  — pointer went down, or moved while down. The component moves the thumb and
//                           tells its parent so the time readout can follow the finger. NOT a seek.
//   OnDragEnd(fraction)   — pointer released. The component commits exactly one seek.
//   OnDragCancel()        — the browser took the pointer away (pointercancel / lost capture without a
//                           pointerup). No seek.
//
// A tap is a pointerdown followed by a pointerup, so it arrives as one OnDragMove and one OnDragEnd:
// tap-to-seek and drag-to-seek are the same path, and a tap still seeks exactly once.
//
// Moves are coalesced to at most one invoke per animation frame. The final OnDragEnd carries the
// release position itself, so a coalesced (dropped) move can never lose the position that is committed.
//
// The fraction is measured against the TRACK element (the visible bar), not the padded hit area, so the
// seek lands where the bar is drawn. Pointer capture keeps the drag alive when the finger slides off the
// hit area vertically or past either end; the fraction is clamped to [0, 1].

const bound = new WeakMap();

function fractionAt(track, clientX) {
  const box = track.getBoundingClientRect();
  if (!box.width) return 0;
  return Math.min(1, Math.max(0, (clientX - box.left) / box.width));
}

export function attach(el, track, dotNetRef) {
  if (!el || !track || !dotNetRef) return;
  detach(el);

  let activePointer = null;
  let last = 0;
  let frame = 0;

  const send = (method, ...args) => {
    // A torn-down circuit or a disposed reference rejects; there is nothing useful to do with that on
    // a wall panel, and an unhandled rejection would only be console noise.
    dotNetRef.invokeMethodAsync(method, ...args).catch(() => { });
  };

  const cancelFrame = () => {
    if (frame) {
      cancelAnimationFrame(frame);
      frame = 0;
    }
  };

  const onDown = (e) => {
    if (activePointer !== null) return;
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    if (el.getAttribute('aria-disabled') === 'true') return;

    activePointer = e.pointerId;
    try { el.setPointerCapture(e.pointerId); } catch { /* pointer already gone */ }
    e.preventDefault();
    last = fractionAt(track, e.clientX);
    send('OnDragMove', last);
  };

  const onMove = (e) => {
    if (e.pointerId !== activePointer) return;
    last = fractionAt(track, e.clientX);
    if (!frame) {
      frame = requestAnimationFrame(() => {
        frame = 0;
        if (activePointer !== null) send('OnDragMove', last);
      });
    }
  };

  const onUp = (e) => {
    if (e.pointerId !== activePointer) return;
    activePointer = null;
    cancelFrame();
    last = fractionAt(track, e.clientX);
    send('OnDragEnd', last);
  };

  // lostpointercapture also fires after a normal pointerup; activePointer is already null by then, so
  // only a capture lost mid-drag reaches OnDragCancel.
  const onCancel = (e) => {
    if (e.pointerId !== activePointer) return;
    activePointer = null;
    cancelFrame();
    send('OnDragCancel');
  };

  el.addEventListener('pointerdown', onDown);
  el.addEventListener('pointermove', onMove);
  el.addEventListener('pointerup', onUp);
  el.addEventListener('pointercancel', onCancel);
  el.addEventListener('lostpointercapture', onCancel);

  bound.set(el, () => {
    cancelFrame();
    el.removeEventListener('pointerdown', onDown);
    el.removeEventListener('pointermove', onMove);
    el.removeEventListener('pointerup', onUp);
    el.removeEventListener('pointercancel', onCancel);
    el.removeEventListener('lostpointercapture', onCancel);
  });
}

export function detach(el) {
  if (!el) return;
  const remove = bound.get(el);
  if (remove) {
    remove();
    bound.delete(el);
  }
}
