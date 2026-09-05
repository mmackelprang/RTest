// ADR-029 PR 6 — what survives of the browser voicemail player.
//
// Everything that attached to an <audio> element is gone: the console fetches, decodes and plays
// voicemail itself now, through the audio engine, so the browser has no audio to drive. ⛔ Do not
// re-add a play/pause/seek surface here — a second audio path is exactly what D17 was about.
//
// This one function remains because tap-to-seek needs the scrubber's RENDERED GEOMETRY, which has no
// server-side equivalent. The API takes the fraction from there.
export function fractionFromEvent(element, clientX) {
  if (!element) return 0;
  const box = element.getBoundingClientRect();
  if (!box.width) return 0;
  return Math.min(1, Math.max(0, (clientX - box.left) / box.width));
}
