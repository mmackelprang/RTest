using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// The single point at which Radio.Web observes attended event playback (ADR-029 D6), and the home of
/// the two derivations the topbar chip and the voicemail transport would otherwise each invent.
/// </summary>
/// <remarks>
/// ⚠ IT CACHES NOTHING. <see cref="Snapshot"/> reads through to AudioStateStore.EventPlayback, which
/// PHN-1e already keeps correct through three mechanisms this class must not fork: the hub broadcast,
/// the one-shot seed from GET /api/audio/events/current, and the broadcast-wins ordering guard between
/// them. A second cache would be a fourth thing to keep in step, and the first to drift.
///
/// ⚠ WHY IT EXISTS AT ALL, since the store already has the data. AudioStateStore is a SINGLETON, so a
/// component subscribing to it subscribes once PER CIRCUIT. The chip lives in MainLayout (every route)
/// and the transport in VoicemailPlayer, so subscribing both directly would put two handlers per
/// circuit — four with two browsers open — on AudioStateStore.EventPlaybackChanged, whose NotifyAsync
/// awaits only the LAST of them (queue row UI-6). This class subscribes ONCE, in its constructor, for
/// the life of the process, and fans out itself.
///
/// ⚠ AND ITS OWN FAN-OUT IS NOT A COPY OF THE DEFECT. The design handoff says to build this "exactly
/// like PhoneUnreadState"; PhoneUnreadState.Set is Changed?.Invoke(_count) — a plain multicast invoke
/// in which one subscriber throwing SYNCHRONOUSLY starves every subscriber registered after it. This
/// class walks GetInvocationList() and isolates each handler instead. That is NOT a fix for UI-6: the
/// store's own three sites are untouched and still queued. It is a refusal to add a fourth.
/// </remarks>
public sealed class ConsolePlaybackState : IDisposable
{
  private readonly AudioStateStore _store;
  private readonly ILogger<ConsolePlaybackState> _logger;

  /// <summary>Subscribes to the store for the life of the process.</summary>
  /// <param name="store">The store whose EventPlaybackChanged this class is the one subscriber to.</param>
  /// <param name="logger">Used only to report a subscriber that threw, so the others still ran.</param>
  public ConsolePlaybackState(AudioStateStore store, ILogger<ConsolePlaybackState> logger)
  {
    _store = store;
    _logger = logger;
    _store.EventPlaybackChanged += OnStoreChangedAsync;
  }

  /// <summary>Raised after the store's snapshot changes. Subscribers must unsubscribe on dispose.</summary>
  public event Func<Task>? Changed;

  /// <summary>The current attended-playback snapshot, or null when nothing has ever been started.</summary>
  /// <remarks>
  /// ⚠ Null is NOT "nothing is playing". A terminal snapshot — Completed, Stopped or Failed — is
  /// RETAINED until a new playback replaces it, deliberately, because it is the only surface an
  /// acquisition failure can be read from (ADR-029 §8.1). Read <see cref="IsLive"/>, never null-ness.
  /// </remarks>
  public EventPlaybackSnapshotDto? Snapshot => _store.EventPlayback;

  /// <summary>True while the console could still be producing sound for this playback.</summary>
  public bool IsLive => Snapshot?.IsLive == true;

  /// <summary>"Voicemail" or "Message" — the KIND, never the sender (handoff §Cross-3).</summary>
  /// <remarks>
  /// ⚠ A Kind this build has never heard of falls through to "Playing" rather than throwing or
  /// painting a raw wire token on the panel. Same rule the State strings follow and for the same
  /// reason: the wire carries strings so a newer API can add a value without a lockstep Web deploy.
  /// </remarks>
  public string KindLabel => Snapshot?.Kind switch
  {
    "RemoteMedia" => "Voicemail",
    "Speech" => "Message",
    _ => "Playing"
  };

  private async Task OnStoreChangedAsync()
  {
    var handlers = Changed?.GetInvocationList();
    if (handlers is null)
    {
      return;
    }

    foreach (var handler in handlers)
    {
      try
      {
        await ((Func<Task>)handler).Invoke();
      }
      catch (Exception ex)
      {
        // Per subscriber, so one circuit's failure cannot silence another's — including a handler
        // that throws SYNCHRONOUSLY, before its first await, which is the starving half of UI-6.
        _logger.LogWarning(ex, "A console-playback subscriber threw; the others still ran");
      }
    }
  }

  /// <summary>Releases the store subscription.</summary>
  public void Dispose() => _store.EventPlaybackChanged -= OnStoreChangedAsync;
}
