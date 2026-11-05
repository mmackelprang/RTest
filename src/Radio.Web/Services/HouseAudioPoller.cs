using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// Polls the RNET controller for one open panel, and reports each result through
/// <see cref="Updated"/>. Live push does not exist on that port, so state has to be polled.
/// </summary>
/// <remarks>
/// <para>
/// One per panel, created through the <c>Func&lt;HouseAudioPoller&gt;</c> in <c>Program.cs</c>, and
/// disposed by that panel. Sharing one between circuits is what <c>UI-33</c> is about: one panel
/// closing would stop the others.
/// </para>
/// <para>
/// Polls cannot overlap: one loop awaits each poll before it waits for the timer. A
/// <see cref="PeriodicTimer"/> holds at most one tick, so the intervals a slow poll runs through
/// collapse into one following poll, not a backlog.
/// </para>
/// </remarks>
public sealed class HouseAudioPoller : IAsyncDisposable
{
  private readonly HouseAudioApiService _api;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _interval;
  private readonly ILogger<HouseAudioPoller>? _logger;
  private readonly CancellationTokenSource _stopping = new();

  // Guards _timer, _loop, _disposed and _holds.
  private readonly object _sync = new();
  private readonly Dictionary<string, int> _holds = new(StringComparer.Ordinal);

  private PeriodicTimer? _timer;
  private Task? _loop;
  private bool _disposed;

  // Written only by the loop.
  private HouseSystemInfo? _system;
  private IReadOnlyList<HouseZone> _zones = [];
  private IReadOnlyList<HouseSource> _sources = [];

  public HouseAudioPoller(
    HouseAudioApiService api,
    TimeProvider timeProvider,
    TimeSpan interval,
    ILogger<HouseAudioPoller>? logger = null)
  {
    _api = api;
    _timeProvider = timeProvider;
    _interval = interval;
    _logger = logger;
  }

  /// <summary>
  /// Raised after each poll, on a thread-pool thread. A component marshals with <c>InvokeAsync</c>.
  /// ⚠ A handler must not wait for <see cref="DisposeAsync"/>, which waits for the handler.
  /// </summary>
  public event Action<HouseAudioSnapshot>? Updated;

  /// <summary>Polls now, then every interval, until disposed. A second call does nothing.</summary>
  public void Start()
  {
    lock (_sync)
    {
      if (_loop is not null || _disposed)
      {
        return;
      }

      // Created here, not in the loop, so the first interval is measured from this call.
      _timer = new PeriodicTimer(_interval, _timeProvider);
      PeriodicTimer timer = _timer;
      _loop = Task.Run(() => RunAsync(timer, _stopping.Token));
    }
  }

  /// <summary>
  /// While this is held, polls leave the named zone as it was, so that an answer built before a
  /// command landed cannot snap the control back. Dispose it when the command has finished.
  /// </summary>
  public IDisposable HoldZone(string zoneName)
  {
    lock (_sync)
    {
      _holds[zoneName] = _holds.GetValueOrDefault(zoneName) + 1;
    }

    return new ZoneHold(this, zoneName);
  }

  /// <summary>Stops polling and waits for the loop. <see cref="Updated"/> is not raised after this.</summary>
  public async ValueTask DisposeAsync()
  {
    Task? loop;
    lock (_sync)
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      loop = _loop;
    }

    _stopping.Cancel();
    _timer?.Dispose();
    if (loop is not null)
    {
      await loop.ConfigureAwait(false);
    }

    _stopping.Dispose();
  }

  private async Task RunAsync(PeriodicTimer timer, CancellationToken ct)
  {
    try
    {
      do
      {
        await PollAsync(ct).ConfigureAwait(false);
      }
      while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      // Disposed. A poll in flight is abandoned, and its result is not reported.
    }
  }

  private async Task PollAsync(CancellationToken ct)
  {
    // Taken before the first request: an answer to a request sent during a hold can arrive after it
    // ends, and that answer is as old as the hold.
    HashSet<string> heldAtStart = HeldZones();

    HouseAudioResult<HouseSystemInfo> system = await _api.GetSystemAsync(ct).ConfigureAwait(false);
    _system = system.Value ?? _system;
    HouseAudioHealth health = system.Health;

    IReadOnlyList<HouseZone>? zones = null;
    IReadOnlyList<HouseSource>? sources = null;
    if (health == HouseAudioHealth.Ok)
    {
      HouseAudioResult<IReadOnlyList<HouseZone>> zonesResult = await _api.GetZonesAsync(ct).ConfigureAwait(false);
      health = zonesResult.Health;
      zones = zonesResult.Value;
    }

    if (health == HouseAudioHealth.Ok)
    {
      HouseAudioResult<IReadOnlyList<HouseSource>> sourcesResult =
        await _api.GetSourcesAsync(ct).ConfigureAwait(false);
      health = sourcesResult.Health;
      sources = sourcesResult.Value;
    }

    if (health == HouseAudioHealth.Ok)
    {
      heldAtStart.UnionWith(HeldZones());
      _zones = KeepHeldZones(zones!, heldAtStart);
      _sources = sources!;
    }

    // Disposal is what cancels ct, and nothing is reported after it.
    ct.ThrowIfCancellationRequested();
    Raise(new HouseAudioSnapshot(health, _system, _zones, _sources, Stale: health != HouseAudioHealth.Ok));
  }

  private IReadOnlyList<HouseZone> KeepHeldZones(IReadOnlyList<HouseZone> polled, HashSet<string> held)
  {
    if (held.Count == 0)
    {
      return polled;
    }

    // Matched on the ids, which unlike names the controller cannot report twice or as null.
    return polled
      .Select(zone => !held.Contains(zone.Name)
        ? zone
        : _zones.FirstOrDefault(shown => shown.ControllerId == zone.ControllerId && shown.ZoneId == zone.ZoneId) ?? zone)
      .ToList();
  }

  private HashSet<string> HeldZones()
  {
    lock (_sync)
    {
      return new HashSet<string>(_holds.Keys, StringComparer.Ordinal);
    }
  }

  private void Raise(HouseAudioSnapshot snapshot)
  {
    try
    {
      Updated?.Invoke(snapshot);
    }
    catch (Exception ex)
    {
      // One broken panel must not end the polling it depends on to recover.
      _logger?.LogWarning("A House Audio update handler threw {ExceptionType}", ex.GetType().Name);
    }
  }

  private void ReleaseHold(string zoneName)
  {
    lock (_sync)
    {
      int remaining = _holds[zoneName] - 1;
      if (remaining == 0)
      {
        _holds.Remove(zoneName);
      }
      else
      {
        _holds[zoneName] = remaining;
      }
    }
  }

  private sealed class ZoneHold(HouseAudioPoller poller, string zoneName) : IDisposable
  {
    private int _released;

    public void Dispose()
    {
      if (Interlocked.Exchange(ref _released, 1) == 0)
      {
        poller.ReleaseHold(zoneName);
      }
    }
  }
}
