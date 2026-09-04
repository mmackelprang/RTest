using Microsoft.AspNetCore.Components.Server.Circuits;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// ADR-029 D7 §7.3 — the last-circuit-closed backstop for attended playback, and the first
/// CircuitHandler this application has ever had.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ It fires on "no circuits remain", NOT on "the circuit that started it left". The original design
/// matched a departing circuit against an OwnerToken; ADR-029 ⟨A1·4⟩ deleted both, because a kiosk
/// refresh drops one circuit and opens another, so an owner-matched handler would stop audio the user
/// is actively watching some minutes later for no visible reason. There is one audio engine and one
/// set of speakers, so there is no owner (§7.4).
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
/// ⚠ This is the WEAKEST of the three defences and must not be trusted as the guarantee. Blazor
/// Server closes a circuit not at tab close but after the disconnect retention window, which THIS
/// APPLICATION CONFIGURES TO 10 MINUTES (<c>Program.cs</c>,
/// <c>DisconnectedCircuitRetentionPeriod</c>) rather than leaving at the framework's 3 — so this is a
/// ten-minute-latency mechanism, not a three-minute one.
/// </para>
/// <para>
/// ⚠ And follow that number through, because the consequence is sharper than the latency alone:
/// <c>GvMedia:MaxPlaybackSeconds</c> ships at 300 s, the retention window is 600 s, and 300 &lt; 600,
/// so at shipped configuration the max-duration cap (§7.1) ALWAYS fires before this backstop can.
/// This is still correct to build — §7.3 requires it, it is defence in depth, and it becomes live the
/// moment either number moves — but a reader must not believe it is what stops a runaway voicemail.
/// The cap is.
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
