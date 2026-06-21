// Tiny interop for the voicemail inline player. The <audio> element does Range
// natively against radio:5004 (ADR D4) — this only bridges events to .NET.
export function attach(audio, dotnet) {
  if (!audio) return null;
  const onTime = () => dotnet.invokeMethodAsync('OnTimeUpdate',
    audio.currentTime || 0, isFinite(audio.duration) ? audio.duration : 0);
  const onEnded = () => dotnet.invokeMethodAsync('OnEnded');
  const onError = () => dotnet.invokeMethodAsync('OnAudioError');
  const onPlaying = () => dotnet.invokeMethodAsync('OnPlaying');
  const onWaiting = () => dotnet.invokeMethodAsync('OnBuffering');
  audio.addEventListener('timeupdate', onTime);
  audio.addEventListener('ended', onEnded);
  audio.addEventListener('error', onError);
  audio.addEventListener('playing', onPlaying);
  audio.addEventListener('waiting', onWaiting);
  return {
    play: () => audio.play().catch(() => dotnet.invokeMethodAsync('OnAudioError')),
    pause: () => audio.pause(),
    // fraction in [0,1] from the tap x over the scrubber width
    seekFraction: (f) => {
      if (isFinite(audio.duration) && audio.duration > 0) {
        audio.currentTime = Math.max(0, Math.min(1, f)) * audio.duration;
      }
    },
    dispose: () => {
      audio.removeEventListener('timeupdate', onTime);
      audio.removeEventListener('ended', onEnded);
      audio.removeEventListener('error', onError);
      audio.removeEventListener('playing', onPlaying);
      audio.removeEventListener('waiting', onWaiting);
    }
  };
}

// Resolve a [0,1] fraction from a tap's clientX over the scrubber's box. Used by
// OnScrubberClick to translate a click into a seek position.
export function fractionFromEvent(el, clientX) {
  const r = el.getBoundingClientRect();
  return r.width > 0 ? (clientX - r.left) / r.width : 0;
}
