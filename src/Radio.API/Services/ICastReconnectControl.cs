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
  /// only a connection it made itself. Also ends the current reconnect episode, so the next drop
  /// starts a fresh window. Never throws.
  /// </summary>
  Task CancelCastReconnectAsync();
}
