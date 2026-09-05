// Screen idle dimmer + sleep navigator for kiosk mode (PR 6 / handoff §P2·1).
//
// Two states:
//   1. Dimmed:        brightness reduced after idle timeout (music continues)
//   2. Sleep route:   navigate to /sleep after longer idle (music continues —
//                     the sleep screen does NOT pause audio; only the explicit
//                     Sleep nav-pill or a server-side SetSleepAsync(true) does).
//
// Removed in PR 6:
//   - The full-page black overlay element. The sleep screen is a Blazor route
//     now (/sleep), so we navigate instead of compositing a div on top of the
//     existing surface. This eliminates the overlay-hack from idle-dimmer.js
//     entirely (handoff §P2·1 acceptance: "No black overlay hack remains in JS").
//
// Kept in PR 6:
//   - The dim step (brightness 0.3 after IDLE_TIMEOUT) — purely cosmetic, music
//     keeps playing, no API call. Undimmed on the next user activity.
//   - The Blazor JS interop bridge (window.radioSleepManager). The bridge no
//     longer mutates DOM; it navigates the route instead.
//     ⚠ CORRECTED (ADR-029 §16.4): this used to say the bridge exists "for
//     callers that drive sleep/wake from server-pushed SleepStateChanged
//     events". No such caller exists. MainLayout.OnSleepStateChanged uses
//     NavigationManager.NavigateTo("/sleep") directly. The only bridge members
//     Blazor invokes are setBlazorRef and wake. See the export block at the
//     bottom of this file for why that mattered.
//
// What counts as activity (both halves matter — each resets dimTimer AND
// sleepTimer, so activity postpones the /sleep navigation as well as undimming):
//   - DOM events, listened for at the bottom of this file: pointerdown, keydown,
//     wheel, and a throttled pointermove.
//   - The physical encoder knobs, via MainLayout calling radioSleepManager.wake
//     ('encoder') from its EncoderHudService.StateChanged subscription (ENC-20).
//     There is no DOM event to listen for in that case and there cannot be: a
//     knob turn reaches the browser as a SignalR push from the API, so nothing
//     is ever dispatched into this document. Before ENC-20 the knobs therefore
//     acted on a screen that stayed dim and still slept on schedule while a hand
//     was on the panel.
//
// wake() needs no change to serve that caller — it already undims and resets the
// timers, and already early-returns on /sleep, which owns its own wake flow.
//
// Exposes window.radioSleepManager for JS interop from Blazor.

// Safe setter for API base URL (called from Blazor JS interop instead of eval).
window.radioSetApiBaseUrl = function (url) {
  window.radioApiBaseUrl = url;
};

(function () {
  const IDLE_TIMEOUT = 5 * 60 * 1000;   // 5 minutes → dim
  const SLEEP_TIMEOUT = 30 * 60 * 1000; // 30 minutes → navigate /sleep
  const DIM_BRIGHTNESS = 0.3;

  let dimTimer = null;
  let sleepTimer = null;
  let dimmed = false;
  let blazorRef = null;

  function isOnSleepRoute() {
    return window.location.pathname === '/sleep';
  }

  function resetTimers() {
    clearTimeout(dimTimer);
    clearTimeout(sleepTimer);
    // No timers fire while we're on /sleep — the route owns its own lifecycle
    // and the next user tap navigates home (which triggers a fresh resetTimers
    // on mount of MainLayout).
    if (isOnSleepRoute()) return;
    dimTimer = setTimeout(dim, IDLE_TIMEOUT);
    sleepTimer = setTimeout(function () { navigateToSleep('idle'); }, SLEEP_TIMEOUT);
  }

  function undim() {
    if (dimmed) {
      document.body.style.filter = '';
      document.body.style.transition = 'filter 0.5s ease';
      dimmed = false;
    }
  }

  function dim() {
    if (isOnSleepRoute()) return;
    document.body.style.transition = 'filter 2s ease';
    document.body.style.filter = 'brightness(' + DIM_BRIGHTNESS + ')';
    dimmed = true;
  }

  // Navigate to /sleep. The Blazor route owns visual presentation and tap-to-
  // wake; this function never composites overlays. Visual-only navigation —
  // does NOT call SystemApi.SetSleepAsync because idle-induced navigation must
  // not pause playback. The Sleep page itself handles the explicit-wake flow.
  function navigateToSleep(source) {
    if (isOnSleepRoute()) return;
    undim(); // Clear filter so the sleep screen renders at full opacity.
    clearTimeout(dimTimer);
    clearTimeout(sleepTimer);
    window.location.href = '/sleep';
  }

  // Deep sleep: navigate to /sleep AND pause audio.
  //
  // ⚠ NOT from idle - that part was always right, and navigateToSleep above is
  // the idle path. ⚠ But "called from Blazor (MainLayout sleep button, server
  // push)" was WRONG and is corrected here (ADR-029 §16.4): this function has
  // ZERO callers in the tracked tree. The Sleep pill calls
  // SystemApi.SetSleepAsync then NavigationManager.NavigateTo, and the server
  // push does the same - neither comes through here. Retained as the recorded
  // shape of the JS -> Blazor path, not because anything walks it.
  function enterSleep(source) {
    if (isOnSleepRoute()) return;
    undim();
    clearTimeout(dimTimer);
    clearTimeout(sleepTimer);

    // Server already paused audio in this path, so we only notify Blazor for
    // user-initiated wake. The pause-on-button path is handled server-side via
    // SystemApi.SetSleepAsync(true) from the button handler.
    if (source !== 'server' && blazorRef) {
      blazorRef.invokeMethodAsync('OnJsSleepRequested', true)
        .catch(function () { /* ignore */ });
    }

    window.location.href = '/sleep';
  }

  function wake(source) {
    // If we're not idle-blanked, just undim and reset the inactivity timers.
    // The Sleep page handles its own wake — we don't drive navigation here.
    undim();
    if (isOnSleepRoute()) {
      // Don't reset timers; the sleep page route is in control.
      return;
    }
    resetTimers();
  }

  var lastPointerMove = 0;

  function onUserActivity() {
    wake('touch');
  }

  function onPointerMove() {
    var now = Date.now();
    if (now - lastPointerMove < 1000) return;
    lastPointerMove = now;
    wake('touch');
  }

  ['pointerdown', 'keydown', 'wheel'].forEach(function (evt) {
    document.addEventListener(evt, onUserActivity, { passive: true });
  });
  document.addEventListener('pointermove', onPointerMove, { passive: true });

  // Expose global API for Blazor JS interop. The implementations no longer
  // mutate the DOM — they navigate routes.
  //
  // ⚠ CORRECTED (ADR-029 §16.4). This comment used to say the shape was
  // preserved "for server-push callers (MainLayout.OnSleepStateChanged hits
  // enterSleep/wake)". It hits NEITHER: OnSleepStateChanged calls
  // NavigationManager.NavigateTo("/sleep") directly, and so does the Sleep
  // pill. The only members Blazor actually invokes are setBlazorRef and wake.
  // enterSleep has ZERO callers in the tracked tree — it is kept as the
  // recorded shape of the JS→Blazor path, not because anything walks it.
  //
  // ⚠ That stale line was load-bearing: ADR-029 §7.5 cited this file as proof
  // the idle timer reached the server, built a stop condition on it, and the
  // stop then never fired for the case it was written about. See
  // navigateToSleep above — the idle path deliberately calls nothing.
  window.radioSleepManager = {
    enterSleep: enterSleep,         // explicit-action sleep → /sleep; no callers today
    wake: wake,                     // reset dim timer; sleep page handles its own wake
    isSleeping: isOnSleepRoute,
    isScreenBlanked: function () { return false; }, // overlay hack removed
    setBlazorRef: function (ref) { blazorRef = ref; }
  };

  resetTimers();
})();
