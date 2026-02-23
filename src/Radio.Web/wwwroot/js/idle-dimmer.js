// Screen idle dimmer for kiosk mode
// Reduces brightness after 5 minutes of no interaction, restores on touch/mouse
(function () {
  const IDLE_TIMEOUT = 5 * 60 * 1000; // 5 minutes
  const DIM_BRIGHTNESS = 0.3;
  let timer = null;
  let dimmed = false;

  function resetTimer() {
    if (dimmed) {
      document.body.style.filter = '';
      document.body.style.transition = 'filter 0.5s ease';
      dimmed = false;
    }
    clearTimeout(timer);
    timer = setTimeout(dim, IDLE_TIMEOUT);
  }

  function dim() {
    document.body.style.transition = 'filter 2s ease';
    document.body.style.filter = `brightness(${DIM_BRIGHTNESS})`;
    dimmed = true;
  }

  ['pointerdown', 'pointermove', 'keydown', 'wheel'].forEach(evt =>
    document.addEventListener(evt, resetTimer, { passive: true })
  );

  resetTimer();
})();
