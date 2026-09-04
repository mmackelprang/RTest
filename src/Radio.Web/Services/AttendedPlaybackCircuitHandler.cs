using Microsoft.AspNetCore.Components.Server.Circuits;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// ADR-029 D7 §7.3 on ⟨A2⟩'s corrected reasoning — the last-circuit-closed stop for attended
/// playback, and the first CircuitHandler this application has ever had.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ It fires on "no circuits remain", NOT on "the circuit that started it left". ADR-029 ⟨A1·4⟩
/// deleted the owner token because there is one audio engine and one set of speakers, so there is
/// no owner (§7.4).
/// </para>
/// <para>
/// ⚠ <b>A REFRESH STOPS THE AUDIO, AND THAT IS THE POINT — owner decision <c>D30</c>, 2026-09-04:</b>
/// <i>"If the page reloads mid-voicemail, the audio should fail. If the user wants to hear it they
/// can replay."</i> Measured on the appliance the same day: reloading the only browser goes
/// 1 → 0 → 1 — CLOSE THEN OPEN, not the other way round — and this handler stops the playback at the
/// zero, ~0.4 s before the replacement circuit arrives. <b>Do not "fix" that.</b> An earlier revision
/// of this remark called it a defect and pointed at plan PHN-1e U3; U3 was right to make it an
/// on-box check and wrong about which way it would go, and <c>D30</c> settled the direction. ADR-029
/// §16.3 enumerates every firing path against <c>D30</c> and finds three wanted (a reload of the
/// only browser, the last browser leaving, the idle timer's hard navigation) and none unwanted.
/// </para>
/// <para>
/// ⚠ <b>One acknowledged divergence, recorded rather than hidden (§16.3, P4):</b> with a second
/// browser open a kiosk reload goes 2 → 1 → 2 and nothing stops, so this implements <c>D30</c>
/// literally only in the single-browser case — which is this appliance's resting state. Closing that
/// gap needs to know WHICH circuit started the playback, i.e. the owner token ADR-029 ⟨A1·4⟩ deleted
/// and <c>D30</c>'s framing excludes again. The divergence is benign: a second client still has the
/// transport, so the audio never becomes unattended.
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
/// ⚠ <b>Its role is PROMOTED, not weakest.</b> §7.3 described this as "the weakest of the three
/// defences, worth having, not worth trusting", and ADR-029 §16.3 struck that: under <c>D30</c> this
/// is the mechanism that implements the owner's rule in this box's resting configuration, and §7.1's
/// max-duration cap is the backstop behind IT rather than the other way round.
/// </para>
/// <para>
/// ⚠ <c>DisconnectedCircuitRetentionPeriod</c> (<c>Program.cs</c>, 10 minutes) is NOT what gates
/// this. It covers UNEXPECTED disconnects — a network blip, a killed browser. A graceful close
/// (reload, navigate away from the app, tab close) disposes the circuit at once and
/// <c>OnCircuitClosedAsync</c> runs then. Zero-latency on the path that actually happens; ten-minute
/// only on an unexpected drop. <c>OnCircuitClosedAsync</c> is therefore <b>bimodal and carries no
/// indication of which mode it is in</b> (§16.2) — a ~1500× latency spread on the same callback.
/// </para>
/// <para>
/// ⚠ <b>The two numbers ARE coupled, in one direction only, and nothing enforces it.</b> The
/// graceful path has no window at all, so ordering says nothing there — but the non-graceful path
/// (§16.3, P6) is harmless <i>because</i> <c>GvMedia:MaxPlaybackSeconds</c> (300) is below the
/// retention period (600): the audio has already been capped five minutes before the eviction fires,
/// so the stop lands on a playback that has ended. Raise the cap past ten minutes — plausible, for a
/// long voicemail — and a behaviour nobody has ever seen appears silently. The cap clamps at a
/// minimum of 1 and has no maximum; the retention period lives in <c>Radio.Web</c> and is known to
/// nothing in <c>Radio.API</c>. Recorded here because it is the only place both numbers are in view.
/// ⚠ And the 600 has never been WATCHED on this box — it is a config value plus documented framework
/// behaviour, marked as a derivation for the reason §16.7 gives.
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
  /// <remarks>
  /// ⚠ <b>Guarded WHOLE, not just around the I/O.</b> The framework's own rule is that <i>"if a
  /// custom circuit handler's methods throw an unhandled exception, the exception is fatal to the
  /// circuit"</i>. An earlier revision wrapped only <see cref="StopAttendedPlaybackAsync"/>'s HTTP
  /// call, leaving the store read and the log line outside — guarded where a throw looked likely
  /// rather than where the hazard is. That mattered little for something §7.3 called "not worth
  /// trusting"; ADR-029 §16.3 promotes this rule to the mechanism that implements <c>D30</c>, so it
  /// is guarded properly now. The realistic loss from a throw here is bounded and worth saying so:
  /// this circuit is already closing, so what is lost is the stop, not a working session.
  /// </remarks>
  public override async Task OnCircuitClosedAsync(
    Circuit circuit, CancellationToken cancellationToken)
  {
    try
    {
      var remaining = Interlocked.Decrement(ref _openCircuits);

      if (remaining < 0)
      {
        // A close with no matching open. Not reachable today — this handler is registered before the
        // app serves a request — but a count left negative would make the "== 0" test below
        // unreachable for the life of the process: a backstop that has silently stopped
        // backstopping. Reset loudly.
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
    catch (Exception ex)
    {
      // Unreachable through StopAttendedPlaybackAsync, which catches its own. This is the backstop
      // for everything else in the method, and for whatever a later edit adds to it.
      _logger.LogWarning(ex, "Error handling a closed circuit; live-circuit count may be off by one");
    }
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
