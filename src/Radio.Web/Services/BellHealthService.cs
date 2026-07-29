using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// Owns the single app-wide poll of RotaryPhone's phone system-status endpoint and
/// exposes the derived <see cref="BellHealth"/> that the topbar /phone nav pill binds
/// to (bell-failure handoff §3.7).
///
/// <para>
/// Why a background poll and not the <c>PhoneUnreadState</c> publish-from-the-page
/// pattern: the fault badge's entire job is to be visible from every page *before*
/// anyone thinks to open /phone. A value published only by <c>PhonePage</c> would stay
/// dark until the user had already navigated to the surface it is meant to send them to.
/// </para>
///
/// <para>
/// Structure mirrors <see cref="GvBridgeStatusService"/> verbatim: singleton, driven as
/// an <see cref="IHostedService"/> so the host owns the loop lifecycle, resolving the
/// scoped/typed <see cref="PhoneApiService"/> through a scope per poll (a singleton
/// cannot inject a typed HttpClient — ADR-022 §6.2).
/// </para>
///
/// <para>
/// Radio.Web is UI-only with respect to phone functionality: this reads RotaryPhone.API
/// over REST and registers no RotaryPhone services.
/// </para>
/// </summary>
public sealed class BellHealthService : IHostedService, IAsyncDisposable
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<BellHealthService> _logger;
  private readonly int _pollSeconds;
  // Guards Start() idempotency across threads (0 = not started, 1 = started).
  private int _started;
  // 0 = running, 1 = stop already requested. Guards against StopAsync (host shutdown)
  // and DisposeAsync (DI teardown) both cancelling/awaiting the same loop.
  private int _stopped;
  private PeriodicTimer? _timer;
  private Task? _loop;
  private CancellationTokenSource? _cts;
  // Serializes the compare-and-set in Apply() across the poll loop and every circuit
  // that calls Publish(). See the comment there for why this is not just paranoia.
  private readonly object _gate = new();

  /// <summary>
  /// Current bell health. Seeded <see cref="BellHealth.Unknown"/> so nothing alarms
  /// before the first poll returns (handoff §7m — never alarm on absence of evidence).
  /// </summary>
  public BellHealth Health { get; private set; } = BellHealth.Unknown;

  /// <summary>Raised only when <see cref="Health"/> actually changes value.</summary>
  public event Action<BellHealth>? HealthChanged;

  public BellHealthService(
    IServiceScopeFactory scopeFactory,
    ILogger<BellHealthService> logger,
    int pollSeconds = 15)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _pollSeconds = pollSeconds <= 0 ? 15 : pollSeconds;
  }

  Task IHostedService.StartAsync(CancellationToken cancellationToken)
  {
    Start();
    return Task.CompletedTask;
  }

  Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopLoopAsync();

  /// <summary>
  /// Starts the background poll loop. Idempotent and thread-safe; a second call is a
  /// no-op. Public so tests/integration code can trigger it explicitly; the host
  /// normally drives it via <see cref="IHostedService.StartAsync"/>.
  /// </summary>
  public void Start()
  {
    if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
    {
      return;
    }
    _cts = new CancellationTokenSource();
    _timer = new PeriodicTimer(TimeSpan.FromSeconds(_pollSeconds));
    _loop = Task.Run(() => PollLoopAsync(_cts.Token));
  }

  /// <summary>
  /// Pushes a status snapshot the app already has in hand (PhonePage polls the same
  /// endpoint every 5s) so the topbar badge tracks the page instead of lagging it by up
  /// to a full poll interval. Matters most on recovery: a badge that outlives the fault
  /// it points at is the same "confidently wrong screen" failure this work exists to fix.
  /// </summary>
  public void Publish(PhoneSystemStatusDto? status) =>
    Apply(BellHealthRules.FromSystemStatus(status));

  private async Task StopLoopAsync()
  {
    // Idempotent: only the first caller (StopAsync OR DisposeAsync, whichever wins)
    // cancels + awaits the loop. The CTS is disposed in DisposeAsync, so tolerate
    // ObjectDisposedException if teardown ordering races.
    if (Interlocked.Exchange(ref _stopped, 1) != 0)
    {
      return;
    }
    try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* already torn down */ }
    if (_loop != null)
    {
      try { await _loop; } catch { /* ignore — cancellation/teardown */ }
    }
  }

  private async Task PollLoopAsync(CancellationToken ct)
  {
    // Prime once immediately so the badge doesn't wait a full interval after boot.
    await PollOnceAsync(ct);
    try
    {
      while (await _timer!.WaitForNextTickAsync(ct))
      {
        await PollOnceAsync(ct);
      }
    }
    catch (OperationCanceledException) { /* shutting down */ }
  }

  private async Task PollOnceAsync(CancellationToken ct)
  {
    try
    {
      using var scope = _scopeFactory.CreateScope();
      var api = scope.ServiceProvider.GetRequiredService<PhoneApiService>();
      Publish(await api.GetSystemStatusAsync(ct));
    }
    catch (Exception ex)
    {
      // A failed fetch is NOT a bell fault — we simply don't know. Falling back to
      // Unknown keeps the badge dark rather than alarming on a Radio.Web-side or
      // network problem that says nothing about the phone (handoff §7m).
      _logger.LogDebug(ex, "Bell health poll failed; treating as unknown");
      Apply(BellHealth.Unknown);
    }
  }

  private void Apply(BellHealth health)
  {
    // Unlike GvBridgeStatusService — whose only writer is its own sequential poll loop
    // — this service has several: the poll loop below, plus every open /phone circuit's
    // 5s timer and its Task.Run-wrapped SystemStatusChanged handler, both of which reach
    // Publish(). Without the gate the compare-then-set-then-notify is not atomic, so
    // overlapping writers double-fire HealthChanged — not theoretical: with the window
    // widened, 128 concurrent identical publishes raised 10 notifications instead of 1
    // (see BellHealthServiceTests.Publish_ConcurrentIdenticalTransitions_RaisesExactlyOnce).
    // Each spurious notification is a full MainLayout re-render.
    lock (_gate)
    {
      if (health == Health)
      {
        // Suppress no-op notifications so MainLayout doesn't re-render every interval.
        return;
      }
      Health = health;
    }

    // Fire OUTSIDE the gate. Subscribers are per-circuit Blazor components that call
    // InvokeAsync(StateHasChanged); holding a lock across arbitrary handler code would
    // let one slow circuit stall the poll loop and every other publisher.
    HealthChanged?.Invoke(health);
  }

  public async ValueTask DisposeAsync()
  {
    await StopLoopAsync();   // no-op if the host already stopped us
    _timer?.Dispose();
    _cts?.Dispose();
  }
}
