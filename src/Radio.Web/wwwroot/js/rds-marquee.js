// rds-marquee.js — offset-preserving scroll engine for the RDS RadioText
// ticker (RdsScrollMarquee.razor).
//
// Why JS instead of the original pure-CSS @keyframes marquee: the keyframes
// animated translateX(100%) → translateX(-100%), percentages OF THE TRACK'S
// OWN WIDTH, with the duration recomputed per render. Every buffer append
// changed both the track width and the duration while the animation's start
// time stayed fixed, so the elapsed fraction was reinterpreted against new
// geometry — the track visibly snapped many characters at once (the reported
// "jerk"). The percent keyframes also made the real px/s speed vary with
// buffer length (travel 2×trackWidth over a duration computed for
// container+trackWidth), and each cycle began with a long blank lead-in.
//
// This engine drives the SAME transform via the Web Animations API — still
// composited off the main thread (important on the Intel N100 where the
// audio pipeline is main-thread sensitive; see HANDOFF-rds-accumulating-
// scroll §4) — but owns the scroll offset explicitly:
//
//   offset o (px): visual transform = translateX(-o)
//     o = -containerWidth → text head sits just off the RIGHT edge
//     o = 0              → text head at the LEFT edge (home position)
//     o = trackWidth     → text fully off the LEFT edge
//
// Each animation "leg" runs from the current offset to trackWidth at a
// constant px/s; on finish the offset wraps to -containerWidth and the next
// leg starts (classic ticker loop — the full rolling history replays each
// cycle).
//
// The track is two spans: a HEAD (station name + separator, may be empty)
// and a BODY (the RadioText buffer). Blazor classifies each BODY change and
// calls update(); the engine then, in ONE task, writes the new text into both
// spans, re-measures, and restarts the leg FROM THE PRESERVED OFFSET with two
// compensations: the head's width change (a rolling-PS page of a different
// length must not move the body), and the width of any characters evicted
// from the front of the body (trimmed count × the body's measured per-char
// width — the track is monospace). Setting the text here rather than from
// Blazor is what keeps the compensation on the same frame as the text
// change (AUD-63).
//
// Instances are keyed by a C#-generated numeric id (never by element), so
// dispose() works even after Blazor has detached the elements.

const instances = new Map();

function prefersReducedMotion() {
  return typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

function measure(inst) {
  // scrollWidth of the nowrap inline-block track = full content width even
  // while a transform is applied (transforms don't affect layout). The span
  // rects are translated along with the track, but a translation does not
  // change their width.
  inst.trackWidth = inst.track.scrollWidth;
  inst.containerWidth = inst.container.clientWidth;
  inst.headWidth = inst.head ? inst.head.getBoundingClientRect().width : 0;

  const bodyEl = inst.body || inst.track;
  const bodyWidth = inst.body ? inst.body.getBoundingClientRect().width : inst.trackWidth;
  const bodyChars = (bodyEl.textContent || '').length;
  inst.bodyCharWidth = bodyChars > 0 ? bodyWidth / bodyChars : 0;
}

function setText(el, text) {
  if (el && typeof text === 'string' && el.textContent !== text) {
    el.textContent = text;
  }
}

// Current offset in px, derived from the running leg. Works while paused
// (currentTime freezes) and between legs (falls back to the stored offset).
function currentOffset(inst) {
  if (!inst.anim || inst.anim.currentTime == null) {
    return inst.offset;
  }
  return inst.legStart + inst.speed * (inst.anim.currentTime / 1000);
}

function cancelAnim(inst) {
  if (inst.anim) {
    inst.anim.onfinish = null;
    inst.anim.cancel();
    inst.anim = null;
  }
}

function runLeg(inst) {
  let from = inst.offset;
  const to = inst.trackWidth;
  if (to - from <= 0) {
    // Degenerate (start() clamps, so only reachable via zero-width
    // measurements) — wrap to the entry position; bail if still empty.
    inst.offset = -inst.containerWidth;
    from = inst.offset;
    if (to - from <= 0) {
      return;
    }
  }
  const distance = to - from;
  inst.legStart = from;
  inst.anim = inst.track.animate(
    [
      { transform: `translateX(${-from}px)` },
      { transform: `translateX(${-to}px)` },
    ],
    { duration: (distance / inst.speed) * 1000, easing: 'linear', fill: 'forwards' });
  inst.anim.onfinish = () => {
    // Text fully exited left — re-enter from the right edge (ticker loop).
    inst.offset = -inst.containerWidth;
    runLeg(inst);
  };
  if (inst.paused) {
    inst.anim.pause();
  }
}

function start(inst) {
  cancelAnim(inst);

  const fits = inst.trackWidth <= inst.containerWidth;

  if (prefersReducedMotion()) {
    // HANDOFF §7 — no motion; the CSS fallback (overflow-x: auto) lets the
    // user read the buffer at their own pace. Static-centered when it fits.
    inst.container.classList.toggle('is-static', fits);
    inst.track.style.transform = '';
    inst.offset = 0;
    return;
  }

  inst.container.classList.toggle('is-static', fits);
  if (fits) {
    // Static-fit branch decided from REAL measured widths — the old C#-side
    // approximation (7 px/char against a hard-coded 420 px container) badly
    // mismeasured the in-card 14 px/0.18em typography and clipped text that
    // it wrongly classified as fitting.
    inst.track.style.transform = '';
    inst.offset = 0;
    return;
  }

  // Clamp the preserved offset into the leg's domain.
  if (inst.offset < -inst.containerWidth || inst.offset >= inst.trackWidth) {
    inst.offset = -inst.containerWidth;
  }
  runLeg(inst);
}

/**
 * Attach the engine to a freshly-rendered marquee. The spans already hold
 * the text Blazor mounted them with; init only measures.
 * @param {number} id C#-generated instance id.
 * @param {Element} container .rcp-rds-rt-scroll element.
 * @param {Element} track .rcp-rds-rt-track element.
 * @param {Element|null} head .rcp-rds-rt-head span (station name + separator).
 * @param {Element|null} body .rcp-rds-rt-body span (RadioText buffer).
 * @param {number} speedPxPerSec configured scroll speed.
 */
export function init(id, container, track, head, body, speedPxPerSec) {
  if (!container || !track) {
    return;
  }
  dispose(id); // idempotent re-init

  const inst = {
    container,
    track,
    head: head || null,
    body: body || null,
    speed: Math.max(1, speedPxPerSec || 40),
    offset: 0,       // start at the home position — new text readable immediately
    legStart: 0,
    anim: null,
    paused: false,
    trackWidth: 0,
    containerWidth: 0,
    headWidth: 0,
    bodyCharWidth: 0,
    onPause: null,
    onMaybeResume: null,
    resizeObserver: null,
  };

  // Pause-on-hover / pause-on-focus (HANDOFF §3 state C). WAAPI pause holds
  // the exact current time; resume continues from the same spot.
  inst.onPause = () => {
    inst.paused = true;
    if (inst.anim) {
      inst.anim.pause();
    }
  };
  inst.onMaybeResume = () => {
    // Only resume when the strip is neither hovered nor focus-holding.
    if (container.matches(':hover') || container.matches(':focus-within')
      || container === document.activeElement) {
      return;
    }
    inst.paused = false;
    if (inst.anim) {
      inst.anim.play();
    }
  };
  container.addEventListener('pointerenter', inst.onPause);
  container.addEventListener('pointerleave', inst.onMaybeResume);
  container.addEventListener('focusin', inst.onPause);
  container.addEventListener('focusout', inst.onMaybeResume);

  // The container is a flex item sharing its row with the conditionally-
  // rendered PTY chip (RdsCard) — the chip appearing/disappearing changes
  // the available width with NO text/speed change to trigger a re-measure.
  // (The old percent-based CSS keyframes adapted implicitly; an explicit
  // pixel engine must observe.) Re-measure + restart the leg from the
  // preserved offset whenever the measured width actually changes.
  if (typeof ResizeObserver === 'function') {
    inst.resizeObserver = new ResizeObserver(() => {
      if (!instances.has(id)) {
        return;
      }
      const width = inst.container.clientWidth;
      if (width === inst.containerWidth) {
        return; // includes the initial fire-on-observe
      }
      inst.offset = currentOffset(inst);
      cancelAnim(inst);
      measure(inst);
      start(inst);
    });
    inst.resizeObserver.observe(container);
  }

  instances.set(id, inst);
  measure(inst);
  start(inst);
}

/**
 * Re-sync after Blazor classified a change in the head/body text and/or the
 * configured speed. Writes the text, re-measures and restarts the leg in one
 * task, so the compensated offset lands on the same frame as the new text.
 * @param {number} id instance id from init().
 * @param {string} mode 'append' (body continuation: preserve offset,
 *   compensate the front-trim), 'swap' (body substituted in place and/or
 *   only the head changed: preserve offset), 'speed' (text unchanged, speed
 *   changed), or 'reset' (unrelated body: restart from the home position).
 *   Every mode but 'reset' also absorbs the head's width change.
 * @param {number} trimmedChars characters evicted from the FRONT of the
 *   body text since the last sync (mode 'append' only).
 * @param {string} headText the head span's new text (null: leave as is).
 * @param {string} bodyText the body span's new text (null: leave as is).
 * @param {number} speedPxPerSec current configured speed.
 */
export function update(id, mode, trimmedChars, headText, bodyText, speedPxPerSec) {
  const inst = instances.get(id);
  if (!inst) {
    return;
  }

  // Freeze the current offset BEFORE cancelling the running leg.
  inst.offset = currentOffset(inst);
  cancelAnim(inst);

  const prevHeadWidth = inst.headWidth;
  const prevBodyCharWidth = inst.bodyCharWidth;

  setText(inst.head, headText);
  setText(inst.body, bodyText);

  inst.speed = Math.max(1, speedPxPerSec || inst.speed);
  measure(inst);

  if (mode === 'reset') {
    inst.offset = 0;
  } else if (mode !== 'speed') {
    // The head sits ahead of the body on the track: if it grew by Δ, every
    // body glyph moved right by Δ, so the offset moves by Δ to hold them.
    inst.offset += inst.headWidth - prevHeadWidth;
    if (mode === 'append' && trimmedChars > 0) {
      // Front-evicted glyphs are gone from the DOM; shift the offset left by
      // their width (monospace ⇒ trimmedChars × per-char width) so the
      // glyphs still on screen do not move. Priced at the PREVIOUS body char
      // width — the width the trimmed glyphs actually had.
      inst.offset -= trimmedChars * (prevBodyCharWidth || inst.bodyCharWidth);
    }
  }
  // 'speed' keeps the offset untouched.

  start(inst);
}

/**
 * Tear down an instance: cancel the animation, remove listeners, drop all
 * element references. Safe to call with an unknown/already-disposed id and
 * safe after Blazor detached the elements.
 */
export function dispose(id) {
  const inst = instances.get(id);
  if (!inst) {
    return;
  }
  cancelAnim(inst);
  if (inst.resizeObserver) {
    inst.resizeObserver.disconnect();
    inst.resizeObserver = null;
  }
  inst.container.removeEventListener('pointerenter', inst.onPause);
  inst.container.removeEventListener('pointerleave', inst.onMaybeResume);
  inst.container.removeEventListener('focusin', inst.onPause);
  inst.container.removeEventListener('focusout', inst.onMaybeResume);
  instances.delete(id);
}

// Test seam (also handy for kiosk console debugging): expose the live
// instance state without letting callers mutate it.
export function _debugState(id) {
  const inst = instances.get(id);
  if (!inst) {
    return null;
  }
  return {
    offset: currentOffset(inst),
    trackWidth: inst.trackWidth,
    containerWidth: inst.containerWidth,
    headWidth: inst.headWidth,
    bodyCharWidth: inst.bodyCharWidth,
    headText: inst.head ? inst.head.textContent : null,
    bodyText: inst.body ? inst.body.textContent : null,
    speed: inst.speed,
    paused: inst.paused,
    isStatic: inst.container.classList.contains('is-static'),
    running: !!inst.anim,
  };
}
