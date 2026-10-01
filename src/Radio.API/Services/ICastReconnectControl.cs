namespace Radio.API.Services;

/// <summary>
/// AUD-37 (review M1). Lets a user-facing output or Cast action take the Cast output over from
/// the automatic reconnect, so the reconnect is never a second connecting party (AUD-85).
/// </summary>
public interface ICastReconnectControl
{
  /// <summary>
  /// Cancels the running Cast reconnect watcher, if any, and waits for it to finish — up to a
  /// short bound (3 s), after which it returns regardless; a watcher still finishing then removes
  /// only a connection it made itself, unless Cast has become the active output, in which case it
  /// keeps that connection for the Cast choice. Also ends the current reconnect episode, so the
  /// next drop starts a fresh window. Never throws.
  /// </summary>
  /// <remarks>
  /// The run it cancels may already carry the mark of an earlier
  /// <see cref="CancelCastReconnectForCastPickAsync"/> of its own device that answered false (the
  /// pick returned 409) — the mark is not cleared. Such a run, once its connect comes back, keeps
  /// and starts its connection while the local output is still active; its switch back to Cast is
  /// conditional on no output selection since the drop, so after this call's action (an output
  /// selection) it is refused, and the run then tears the connection down. Until then Cast can play
  /// beside the local output.
  /// </remarks>
  Task CancelCastReconnectAsync();

  /// <summary>
  /// AUD-85 (review MEDIUM-1). <see cref="CancelCastReconnectAsync"/> for a Cast pick of
  /// <paramref name="deviceId"/> (<c>POST /api/devices/cast/connect</c>), with two differences:
  /// <list type="bullet">
  /// <item>When the watcher is reconnecting that same device, a connection it has made is kept,
  /// started and switched to (conditional on no output selection since the drop) rather than torn
  /// down, so the pick finds Cast already streaming to its device. A watcher reconnecting another
  /// device is cancelled as usual and removes its own connection.</item>
  /// <item>It waits longer — up to 15 s, a budget sized for the parts of a reconnect that ignore
  /// cancellation, which they are not guaranteed to fit — and reports whether the run finished:
  /// true when no watcher is still running
  /// (including when there was none); false when the bound passed first. A caller seeing false
  /// must not touch the Cast output, which the run still holds; a same-device run that comes out
  /// later still keeps its connection and switches to it (conditionally, as above; and if Cast is
  /// not streaming once switched, it switches back to the local output itself).</item>
  /// </list>
  /// Also ends the current reconnect episode. Never throws.
  /// </summary>
  /// <param name="deviceId">The picked device's id; null or blank cancels as for another device.</param>
  Task<bool> CancelCastReconnectForCastPickAsync(string? deviceId);
}
