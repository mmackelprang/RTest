// Screen idle dimmer + sleep manager for kiosk mode
//
// Three states:
//   1. Dimmed:        brightness reduced after idle timeout (music continues)
//   2. Screen-blanked: black overlay after longer idle (music continues)
//   3. Deep sleep:     black overlay + audio paused (explicit user/server action only)
//
// Key design: idle timeouts NEVER pause audio. Only explicit sleep (button/server)
// pauses playback. This lets the radio play indefinitely while preserving the screen.
//
// Exposes window.radioSleepManager for JS interop from Blazor

// Safe setter for API base URL (called from Blazor JS interop instead of eval)
window.radioSetApiBaseUrl = function (url) {
  window.radioApiBaseUrl = url;
};

(function () {
  const IDLE_TIMEOUT = 5 * 60 * 1000; // 5 minutes → dim
  const BLANK_TIMEOUT = 30 * 60 * 1000; // 30 minutes → screen blank (visual only)
  const DIM_BRIGHTNESS = 0.3;

  let dimTimer = null;
  let blankTimer = null;
  let dimmed = false;
  let screenBlanked = false; // Visual-only blank (idle timeout) — music keeps playing
  let deepSleep = false;     // Full sleep (audio paused) — explicit action only
  let overlay = null;
  let blazorRef = null;

  function createOverlay() {
    if (overlay) return overlay;
    overlay = document.createElement('div');
    overlay.id = 'sleep-overlay';
    overlay.style.cssText =
      'position:fixed;inset:0;z-index:99998;background:#000;' +
      'opacity:0;pointer-events:none;transition:opacity 2s ease;';
    document.body.appendChild(overlay);
    return overlay;
  }

  function resetTimers() {
    clearTimeout(dimTimer);
    clearTimeout(blankTimer);
    dimTimer = setTimeout(dim, IDLE_TIMEOUT);
    blankTimer = setTimeout(function () { blankScreen(); }, BLANK_TIMEOUT);
  }

  function undim() {
    if (dimmed) {
      document.body.style.filter = '';
      document.body.style.transition = 'filter 0.5s ease';
      dimmed = false;
    }
  }

  function dim() {
    if (screenBlanked || deepSleep) return;
    document.body.style.transition = 'filter 2s ease';
    document.body.style.filter = 'brightness(' + DIM_BRIGHTNESS + ')';
    dimmed = true;
  }

  // Screen blank: visual-only, music keeps playing.
  // Triggered by idle timeout — does NOT call the API.
  function blankScreen() {
    if (screenBlanked || deepSleep) return;
    screenBlanked = true;

    // Show black overlay
    var el = createOverlay();
    el.style.pointerEvents = 'auto';
    void el.offsetWidth;
    el.style.opacity = '1';

    // Dim body fully
    document.body.style.transition = 'filter 2s ease';
    document.body.style.filter = 'brightness(0)';
    dimmed = true;

    clearTimeout(dimTimer);
    clearTimeout(blankTimer);
  }

  // Deep sleep: black overlay + audio paused.
  // Only triggered by explicit action (button press or server command).
  function enterSleep(source) {
    if (deepSleep) return;

    // If we're already screen-blanked, upgrade to deep sleep
    if (!screenBlanked) {
      // Show black overlay
      var el = createOverlay();
      el.style.pointerEvents = 'auto';
      void el.offsetWidth;
      el.style.opacity = '1';

      document.body.style.transition = 'filter 2s ease';
      document.body.style.filter = 'brightness(0)';
      dimmed = true;
    }

    deepSleep = true;
    screenBlanked = false; // Upgrade from blank to deep sleep

    // Notify Blazor to pause audio (unless triggered by server — server already knows)
    if (source !== 'server' && blazorRef) {
      blazorRef.invokeMethodAsync('OnJsSleepRequested', true)
        .catch(function () { /* ignore */ });
    }

    clearTimeout(dimTimer);
    clearTimeout(blankTimer);
  }

  function wake(source) {
    if (!screenBlanked && !deepSleep) {
      // Not blanked or sleeping, just undim and reset timers
      undim();
      resetTimers();
      return;
    }

    var wasDeepSleep = deepSleep;
    deepSleep = false;
    screenBlanked = false;
    undim();

    // Hide overlay
    if (overlay) {
      overlay.style.opacity = '0';
      overlay.style.pointerEvents = 'none';
    }

    // Only notify Blazor to resume audio if we were in deep sleep
    // (screen-blank is visual only — nothing to resume)
    if (wasDeepSleep && source !== 'server' && blazorRef) {
      blazorRef.invokeMethodAsync('OnJsSleepRequested', false)
        .catch(function () { /* ignore */ });
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

  // Listen for user activity (pointermove is throttled to avoid excessive timer resets)
  ['pointerdown', 'keydown', 'wheel'].forEach(function (evt) {
    document.addEventListener(evt, onUserActivity, { passive: true });
  });
  document.addEventListener('pointermove', onPointerMove, { passive: true });

  // Expose global API for Blazor JS interop
  window.radioSleepManager = {
    enterSleep: enterSleep,      // Deep sleep (pauses audio) — for button/server
    blankScreen: blankScreen,    // Screen blank only (no audio impact)
    wake: wake,
    isSleeping: function () { return deepSleep; },
    isScreenBlanked: function () { return screenBlanked; },
    setBlazorRef: function (ref) { blazorRef = ref; }
  };

  resetTimers();
})();
