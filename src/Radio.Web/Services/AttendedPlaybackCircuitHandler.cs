using Microsoft.AspNetCore.Components.Server.Circuits;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// ADR-029 D7 §7.3 — the last-circuit-closed backstop for attended playback, and the first
/// CircuitHandler this application has ever had.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ It fires on "no circuits remain", NOT on "the circuit that started it left". ADR-029 ⟨A1·4⟩
/// deleted the owner token because there is one audio engine and one set of speakers, so there is
/// no owner (§7.4).
/// </para>
/// <para>
/// ⚠ THAT DOES NOT MAKE A REFRESH SAFE, and the wording here used to claim it did. Measured on the
/// appliance 2026-09-04: reloading the only browser goes 1 → 0 → 1 — CLOSE THEN OPEN — and this
/// handler stops the playback at the zero, ~0.4 s before the replacement circuit arrives. With two
/// tabs it goes 2 → 1 → 2 and nothing is stopped, which is why the multi-client case looks fine and
/// the single-kiosk case does not. On the one box this ships to, the last-circuit rule has the SAME
/// failure the owner-token rule had. Plan PHN-1e U3 named this and said "stop and re-plan".
/// </para>
/// <para>
/// ⚠ SINGLETON, deliberately. CircuitHandler instances are resolved from each circuit's scope, so a
/// singleton registration hands the SAME instance to every circuit and the count is process-wide. A
/// scoped registration would give every circuit its own counter, each reaching zero when that circuit
/// closes — which is precisely the owner-circuit rule this class exists not to be. If that resolution
/// behaviour ever changes, move the counter into a separate singleton and keep this scoped; the count
/// is the thing that must be shared, not the handler.
/// </para>
/// <para>
/// ⚠ This is the WEAKEST of the three defences and must not be trusted as the guarantee.
/// <c>DisconnectedCircuitRetentionPeriod</c> (<c>Program.cs</c>, 10 minutes) is NOT what gates this.
/// It covers UNEXPECTED disconnects — a network blip, a killed browser. A graceful close (reload,
/// navigate away from the app, tab close) disposes the circuit at once and <c>OnCircuitClosedAsync</c>
/// runs then. Zero-latency on the path that actually happens; ten-minute only on an unexpected drop.
/// </para>
/// <para>
/// ⚠ Do not reason about ordering from 300 &lt; 600. Different origins — the cap runs from playback
/// start, the retention window from disconnect — and on the graceful-close path there is no window at
/// all: measured, this backstop fires on a single-browser reload, immediately, long before the 300 s
/// cap.
/// </para>
/// <para>
/// A Web singleton cannot inject a typed HttpClient, so the API client is resolved through a scope
/// per use — the shape BellHealthService and GvBridgeStatusService already use (ADR-022 §6.2).
/// </para>
/// </remarks>
public sealed class AttendedPlaybackCircuitHandler : CircuitHandler
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly AudioStateStore _store;
  private readonly ILogger<AttendedPlaybackCircuitHandler> _logger;

  private int _openCircuits;

  public AttendedPlaybackCircuitHandler(
    IServiceScopeFactory scopeFactory,
    AudioStateStore store,
    ILogger<AttendedPlaybackCircuitHandler> logger)
  {
    _scopeFactory = scopeFactory;
    _store = store;
    _logger = logger;
  }

  /// <summary>Live circuits. Exposed for tests and for diagnostics, never for a policy decision.</summary>
  internal int OpenCircuits => Volatile.Read(ref _openCircuits);

  /// <inheritdoc />
  public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
  {
    var count = Interlocked.Increment(ref _openCircuits);
    _logger.LogDebug("Circuit opened; {Count} live", count);

    // A circuit opening IS ADR-029 §8.1's re-attach moment: a client has arrived and may be arriving
    // mid-playback. The seed is one-shot per process and never throws, so this is fire-and-forget by
    // design rather than by omission — awaiting it would hold the circuit's start behind an HTTP call
    // to a service that may still be booting, on a deploy that restarts both together.
    _ = SeedAsync();
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public override async Task OnCircuitClosedAsync(
    Circuit circuit, CancellationToken cancellationToken)
  {
    var remaining = Interlocked.Decrement(ref _openCircuits);

    if (remaining < 0)
    {
      // A close with no matching open. Not reachable today — this handler is registered before the
      // app serves a request — but a count left negative would make the "== 0" test below unreachable
      // for the life of the process: a backstop that has silently stopped backstopping. Reset loudly.
      Interlocked.Exchange(ref _openCircuits, 0);
      _logger.LogWarning("Circuit closed with no matching open; live-circuit count reset to zero");
      return;
    }

    if (remaining != 0)
    {
      _logger.LogDebug("Circuit closed; {Count} still live", remaining);
      return;
    }

    // ⚠ The TRANSITION to zero, never an observed zero. Radio.Web restarting while Radio.API keeps
    // playing leaves the count at zero at rest, and nothing about that is a client walking away.
    await StopAttendedPlaybackAsync();
  }

  private async Task SeedAsync()
  {
    try
    {
      using var scope = _scopeFactory.CreateScope();
      var api = scope.ServiceProvider.GetRequiredService<EventPlaybackApiService>();
      await _store.EnsureEventPlaybackSeededAsync(api);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error seeding attended playback state on circuit open");
    }
  }

  private async Task StopAttendedPlaybackAsync()
  {
    // Read from the store rather than re-reading GET /current. The store is fed by the same broadcast
    // a fresh read would be racing, and a stale read costs nothing here: a stop against an id that
    // has already ended answers 404 or 409, and EventPlaybackApiService.StopAsync reports both as a
    // plain false without logging an error.
    if (_store.EventPlayback is not { IsLive: true } snapshot)
    {
      _logger.LogDebug("Last circuit closed; no attended playback to stop");
      return;
    }

    _logger.LogInformation(
      "Last circuit closed with attended playback {Id} still live; stopping it (ADR-029 §7.3)",
      snapshot.Id);

    try
    {
      using var scope = _scopeFactory.CreateScope();
      var api = scope.ServiceProvider.GetRequiredService<EventPlaybackApiService>();
      await api.StopAsync(snapshot.Id);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error stopping attended playback after the last circuit closed");
    }
  }
}
