using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// Owns the single ~10s /api/gvbridge/status poll for the whole app and exposes
/// an observable availability state the Messages UI binds to (reconnecting
/// banner + Send gate). Singleton; resolves GvBridgeApiService via a scope per
/// poll because a singleton cannot inject a scoped/typed HttpClient
/// (ADR-022 §6.2). RadioConsole only reflects state — RotaryPhone does the
/// actual cookie recovery.
///
/// Implemented as an <see cref="IHostedService"/> so the host owns the
/// background-loop lifecycle: the poll loop is started once at app boot
/// (StartAsync) and cancelled + awaited at graceful shutdown (StopAsync). This
/// avoids the "never-disposed singleton leaks its loop" trap of a manual Start()
/// (see also the AddHostedService(sp => GetRequiredService&lt;T&gt;()) memory note).
/// </summary>
public sealed class GvBridgeStatusService : IHostedService, IAsyncDisposable
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<GvBridgeStatusService> _logger;
  private readonly int _pollSeconds;
  // Guards Start() idempotency across threads (0 = not started, 1 = started).
  private int _started;
  private PeriodicTimer? _timer;
  private Task? _loop;
  private CancellationTokenSource? _cts;

  public GvBridgeStatusDto? Current { get; private set; }
  public bool IsAvailable { get; private set; }
  public event Action<GvBridgeStatusDto?>? StatusChanged;

  public GvBridgeStatusService(
    IServiceScopeFactory scopeFactory,
    ILogger<GvBridgeStatusService> logger,
    int pollSeconds = 10)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _pollSeconds = pollSeconds <= 0 ? 10 : pollSeconds;
  }

  Task IHostedService.StartAsync(CancellationToken cancellationToken)
  {
    Start();
    return Task.CompletedTask;
  }

  Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopLoopAsync();

  // 0 = running, 1 = stop already requested. Guards against StopAsync (host
  // shutdown) and DisposeAsync (DI teardown) both trying to cancel/await the
  // same loop — the second caller must be a no-op, not touch a disposed CTS.
  private int _stopped;

  /// <summary>
  /// Starts the background poll loop. Idempotent and thread-safe: a second call
  /// is a no-op. Public so unit/integration code can trigger it explicitly; the
  /// host normally drives it via <see cref="IHostedService.StartAsync"/>.
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

  private async Task StopLoopAsync()
  {
    // Idempotent: only the first caller (StopAsync OR DisposeAsync, whichever
    // wins) cancels + awaits the loop. The CTS is disposed in DisposeAsync, so
    // tolerate ObjectDisposedException if teardown ordering races.
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
    // Prime once immediately so the UI doesn't wait a full interval.
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
      var api = scope.ServiceProvider.GetRequiredService<GvBridgeApiService>();
      var status = await api.GetStatusAsync(ct);
      ApplyStatus(status);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "GV status poll failed; treating as degraded");
      ApplyStatus(null);
    }
  }

  // Test-facing wrapper so unit tests can drive state without a scope factory.
  public void ApplyStatusForTest(GvBridgeStatusDto? status) => ApplyStatus(status);

  private void ApplyStatus(GvBridgeStatusDto? status)
  {
    Current = status;
    IsAvailable = status is { Available: true };
    StatusChanged?.Invoke(status);
  }

  public async ValueTask DisposeAsync()
  {
    await StopLoopAsync();   // no-op if the host already stopped us
    _timer?.Dispose();
    _cts?.Dispose();
  }
}
