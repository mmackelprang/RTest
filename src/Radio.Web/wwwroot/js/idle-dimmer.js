// Screen idle dimmer + sleep manager for kiosk mode
// - Dim: reduces brightness after 5 minutes of no interaction
// - Sleep: black overlay + mutes audio after 30 minutes of no interaction
// - Wake: touch/pointer/key restores screen + unmutes audio
// Exposes window.radioSleepManager for JS interop from Blazor
(function () {
  const IDLE_TIMEOUT = 5 * 60 * 1000; // 5 minutes → dim
  const SLEEP_TIMEOUT = 30 * 60 * 1000; // 30 minutes → sleep
  const DIM_BRIGHTNESS = 0.3;

  function apiUrl(path) {
    return (window.radioApiBaseUrl || '') + path;
  }
  let dimTimer = null;
  let sleepTimer = null;
  let dimmed = false;
  let sleeping = false;
  let overlay = null;

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
    clearTimeout(sleepTimer);
    dimTimer = setTimeout(dim, IDLE_TIMEOUT);
    sleepTimer = setTimeout(function () { enterSleep('idle'); }, SLEEP_TIMEOUT);
  }

  function undim() {
    if (dimmed) {
      document.body.style.filter = '';
      document.body.style.transition = 'filter 0.5s ease';
      dimmed = false;
    }
  }

  function dim() {
    if (sleeping) return;
    document.body.style.transition = 'filter 2s ease';
    document.body.style.filter = 'brightness(' + DIM_BRIGHTNESS + ')';
    dimmed = true;
  }

  function enterSleep(source) {
    if (sleeping) return;
    sleeping = true;

    // Show black overlay
    var el = createOverlay();
    el.style.pointerEvents = 'auto';
    // Force reflow before setting opacity for transition
    void el.offsetWidth;
    el.style.opacity = '1';

    // Dim body as well
    document.body.style.transition = 'filter 2s ease';
    document.body.style.filter = 'brightness(0)';
    dimmed = true;

    // Notify API to mute audio (unless triggered by server)
    if (source !== 'server') {
      fetch(apiUrl('/api/system/sleep'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sleep: true })
      }).catch(function () { /* ignore */ });
    }

    clearTimeout(dimTimer);
    clearTimeout(sleepTimer);
  }

  function wake(source) {
    if (!sleeping) {
      // Not sleeping, just undim and reset timers
      undim();
      resetTimers();
      return;
    }

    sleeping = false;
    undim();

    // Hide overlay
    if (overlay) {
      overlay.style.opacity = '0';
      overlay.style.pointerEvents = 'none';
    }

    // Notify API to unmute (unless triggered by server)
    if (source !== 'server') {
      fetch(apiUrl('/api/system/sleep'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sleep: false })
      }).catch(function () { /* ignore */ });
    }

    resetTimers();
  }

  function onUserActivity() {
    wake('touch');
  }

  // Listen for user activity
  ['pointerdown', 'pointermove', 'keydown', 'wheel'].forEach(function (evt) {
    document.addEventListener(evt, onUserActivity, { passive: true });
  });

  // Expose global API for Blazor JS interop
  window.radioSleepManager = {
    enterSleep: enterSleep,
    wake: wake,
    isSleeping: function () { return sleeping; }
  };

  resetTimers();
})();
