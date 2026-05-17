// Dev-tray triple-tap gesture (PR 6 / handoff §P2·2).
//
// Listens for taps on the invisible 48×48 hit area in the top-right corner of
// the viewport (element marked with `data-dev-gesture`). Three taps inside a
// 1.5-second window call back into Blazor via DotNetObjectReference to toggle
// the dev tray. Anything less than three taps is silently discarded after the
// window lapses.
//
// Loaded as an ES module from MainLayout.OnAfterRenderAsync:
//   const m = await JSRuntime.InvokeAsync("import", "./js/dev-gesture.js");
//   await m.invokeVoidAsync("init", dotNetRef);
//
// MainLayout disposes the module on circuit teardown by calling dispose(),
// which clears the handler and the .NET reference. The module itself stays
// loaded — JS modules are cached by the browser and idempotent re-init is
// supported.

let taps = [];
const WINDOW_MS = 1500;
const REQUIRED_TAPS = 3;
let dotNetRef = null;
let hitArea = null;

function onTap() {
  const now = Date.now();
  // Drop entries older than the window so a slow tap stream never
  // accumulates a false-positive trigger.
  taps = taps.filter(function (t) { return now - t < WINDOW_MS; });
  taps.push(now);
  if (taps.length >= REQUIRED_TAPS) {
    taps = [];
    if (dotNetRef) {
      // Fire-and-forget — the Blazor side toggles tray state and re-renders.
      // Errors here are non-fatal (e.g. circuit was torn down mid-gesture).
      dotNetRef.invokeMethodAsync('ToggleDevTray').catch(function () { /* ignore */ });
    }
  }
}

export function init(ref) {
  // Re-init guard: if a stale ref is still bound, swap it cleanly.
  dispose();
  dotNetRef = ref;
  hitArea = document.querySelector('[data-dev-gesture]');
  if (hitArea) {
    hitArea.addEventListener('click', onTap);
  }
}

export function dispose() {
  if (hitArea) {
    hitArea.removeEventListener('click', onTap);
    hitArea = null;
  }
  dotNetRef = null;
  taps = [];
}
