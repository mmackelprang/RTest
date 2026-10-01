using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Configuration.Models;
using IAppConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.API.Services;

/// <summary>
/// Background service that initializes and starts the audio engine on application startup.
/// Also handles graceful shutdown of the audio engine and automatic source/output selection.
/// </summary>
public class AudioEngineInitializationService : IHostedService, ICastReconnectControl
{
  private readonly ILogger<AudioEngineInitializationService> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly IAudioManager? _audioManager;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IMasterMixer _masterMixer;
  private readonly IAppConfigurationManager? _configManager;
  private readonly IOptions<BluetoothOptions> _bluetoothOptions;
  private readonly IOptions<AudioOutputOptions> _audioOutputOptions;
  private readonly IBluetoothService? _bluetoothService;
  private readonly BluetoothAutoSwitchService? _bluetoothAutoSwitch;
  private readonly GoogleCastOutput? _castOutput;
  private readonly HttpStreamOutput? _httpOutput;
  private readonly IServiceProvider _serviceProvider;

  // Cancelled by StopAsync so the Cast confirm-or-roll-back background work
  // stops instead of racing engine tear-down (it would otherwise still be
  // sitting in its watchdog delay when the host shuts down).
  private readonly CancellationTokenSource _serviceStoppingCts = new();

  // Serialises the local-output rollback so the explicit bail-out paths and the
  // watchdog can never both drive SetActiveOutputAsync.
  private readonly SemaphoreSlim _fallbackLock = new(1, 1);
  private bool _localFallbackApplied;

  // Cancelled once the output question is settled in local's favour. This both
  // retires the watchdog and abandons any still-running Cast connect attempt —
  // without the latter, a connect that completes after the rollback would start
  // streaming to Cast while the local sink is unmuted, recreating the very
  // dual-output bug the startup mute exists to prevent.
  private readonly CancellationTokenSource _castResolvedCts = new();

  /// <summary>
  /// The background "confirm Cast is really streaming, else fall back to local"
  /// task, retained so shutdown can drain it and tests can await the outcome
  /// deterministically instead of racing a fire-and-forget task.
  /// Null when the persisted output was not google-cast.
  /// </summary>
  internal Task? CastAutoConnectTask { get; private set; }

  /// <summary>
  /// How long to let Cast discovery settle before looking in the device cache.
  /// Test seam — production always uses the 3 s default.
  /// </summary>
  internal TimeSpan CastDiscoverySettleDelay { get; set; } = TimeSpan.FromSeconds(3);

  /// <summary>
  /// Overrides <see cref="GoogleCastOutputOptions.StartupConnectTimeoutSeconds"/>.
  /// Test seam so watchdog tests don't wait the production timeout.
  /// </summary>
  internal TimeSpan? CastConnectTimeoutOverride { get; set; }

  /// <summary>
  /// Initializes a new instance of the AudioEngineInitializationService.
  /// </summary>
  public AudioEngineInitializationService(
    ILogger<AudioEngineInitializationService> logger,
    IAudioEngine audioEngine,
    IAudioDeviceManager deviceManager,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IMasterMixer masterMixer,
    IOptions<BluetoothOptions> bluetoothOptions,
    IOptions<AudioOutputOptions> audioOutputOptions,
    IServiceProvider serviceProvider)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _deviceManager = deviceManager;
    _audioPreferences = audioPreferences;
    _masterMixer = masterMixer;
    _bluetoothOptions = bluetoothOptions;
    _audioOutputOptions = audioOutputOptions;
    _serviceProvider = serviceProvider;

    // Try to get IAudioManager (optional)
    _audioManager = serviceProvider.GetService<IAudioManager>();
    _configManager = serviceProvider.GetService<IAppConfigurationManager>();
    _bluetoothService = serviceProvider.GetService<IBluetoothService>();
    _bluetoothAutoSwitch = serviceProvider.GetService<BluetoothAutoSwitchService>();
    _castOutput = serviceProvider.GetService<GoogleCastOutput>();
    _httpOutput = serviceProvider.GetService<HttpStreamOutput>();

    // AUD-84 / AUD-37. A Cast speaker that drops mid-stream must not leave the cabinet
    // muted: selecting Cast muted the local sink, and nothing else would ever unmute it.
    if (_castOutput != null)
    {
      _castOutput.Disconnected += OnCastOutputDisconnected;
    }

    // AUD-81. Resolved here only to construct it at startup: it subscribes to the master mixer in
    // its constructor, and nothing else depends on it, so a lazy singleton would never be built.
    // Failure is logged, not thrown — the console must still start if the follower cannot.
    try
    {
      CastVolumeFollower = serviceProvider.GetService<CastConsoleVolumeFollower>();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Cast console-volume follower unavailable — the console volume will not reach a Cast speaker");
    }
  }

  /// <summary>
  /// The AUD-81 follower, held so its singleton is constructed at startup. Null when it is not
  /// registered or could not be built.
  /// </summary>
  internal CastConsoleVolumeFollower? CastVolumeFollower { get; }

  /// <summary>
  /// The most recently started lost-Cast recovery, so a test can await it rather than
  /// sleep. Nothing in production reads it.
  /// </summary>
  internal Task LastCastLossRecovery { get; private set; } = Task.CompletedTask;

  private void OnCastOutputDisconnected(object? sender, ChromecastDisconnectedEventArgs e)
  {
    // Deliberate disconnects already go through the output gate, which chose the next
    // output itself. Only a LOST connection needs rescuing.
    if (!e.IsConnectionLost)
    {
      return;
    }

    try
    {
      // Queued, not awaited: this runs on the Cast output's loss-handling thread, which
      // still has the dead client to close after raising the event.
      var device = e.Device;
      LastCastLossRecovery = Task.Run(() => RestoreLocalOutputAfterCastLossAsync(device, e.Reason));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Could not start local-output recovery after the Cast connection was lost");
    }
  }

  /// <summary>
  /// Switches the active output back to the local speakers after the Cast connection was
  /// lost mid-stream (AUD-84): unmutes the local sink, and — through the output gate —
  /// persists the local output as the current one, exactly as the startup fallback does.
  /// Does nothing when the active output is no longer Cast. On the production engine the
  /// check and the switch happen under one acquisition of the engine's output lock
  /// (<see cref="SoundFlowAudioEngine.SetActiveOutputIfCurrentAsync"/>), so a switch to a
  /// non-Cast output made in the UI while this was queued is not overridden. It cannot tell
  /// "still the lost Cast session" from "Cast picked again" — a user who re-selects Cast in the
  /// milliseconds between the loss and this running will have it switched back to local. Any
  /// other <see cref="IAudioEngine"/> gets a check-then-switch with a window between the two.
  /// When this recovery itself makes the switch, it starts the AUD-37 reconnect watcher for
  /// <paramref name="device"/> (replacing any earlier one).
  /// </summary>
  internal async Task RestoreLocalOutputAfterCastLossAsync(ChromecastDeviceInfo? device, string? reason)
  {
    var deviceName = device?.FriendlyName;
    CancellationToken ct;
    try
    {
      ct = _serviceStoppingCts.Token;
      await _fallbackLock.WaitAsync(ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
    {
      return; // Shutting down; the host's own teardown owns the outputs now.
    }

    try
    {
      // Null is treated as "still Cast" for the same reason ApplyLocalFallbackAsync does:
      // failing open is what keeps the local sink from staying muted.
      var active = _audioEngine.ActiveOutputId;
      if (!string.IsNullOrEmpty(active) &&
          !string.Equals(active, "google-cast", StringComparison.OrdinalIgnoreCase))
      {
        _logger.LogInformation(
          "Cast connection to {Device} was lost, but the active output is already {ActiveOutput} — leaving it",
          deviceName ?? "<unknown device>", active);
        return;
      }

      var target = await PickLocalOutputDeviceAsync(ct).ConfigureAwait(false);
      if (target == null)
      {
        _logger.LogError(
          "Cast connection to {Device} was lost and no local output device is available — cannot restore local audio",
          deviceName ?? "<unknown device>");
        return;
      }

      _logger.LogWarning(
        "Cast connection to {Device} was lost ({Reason}) — switching to local output \"{DeviceName}\" ({DeviceId})",
        deviceName ?? "<unknown device>", reason, target.Name, target.Id);

      // AUD-37: the epoch after this switch, read under the same lock acquisition, is the
      // reconnect watcher's baseline — any output selection after it retires the watcher.
      int? epochAfterSwitch = null;
      if (_audioEngine is SoundFlowAudioEngine gate)
      {
        epochAfterSwitch = await gate.SetActiveOutputIfCurrentWithEpochAsync("google-cast", target.Id, ct).ConfigureAwait(false);
        if (epochAfterSwitch == null)
        {
          _logger.LogInformation(
            "Cast-loss recovery abandoned: the active output moved to {ActiveOutput} while it was queued",
            _audioEngine.ActiveOutputId);
          return;
        }
      }
      else
      {
        await _audioEngine.SetActiveOutputAsync(target.Id, ct).ConfigureAwait(false);
      }

      // After the gate, not before it as in ApplyLocalFallbackAsync: this also persists
      // CurrentOutput, and must not do so for a switch the gate declined above.
      try
      {
        await _deviceManager.SetOutputDeviceAsync(target.Id, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Non-fatal: the gate has already unmuted the local sink and persisted the output.
        _logger.LogWarning(ex, "Cast-loss recovery: could not select local output device {DeviceId}", target.Id);
      }

      _logger.LogInformation(
        "Local output \"{DeviceName}\" restored and unmuted after the Cast connection was lost", target.Name);

      StartCastReconnectWatcher(device, new CastRecoveryMark(target.Id, epochAfterSwitch));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      // Shutting down — leave state alone.
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to restore local output after the Cast connection was lost");
    }
    finally
    {
      try { _fallbackLock.Release(); }
      catch (ObjectDisposedException) { /* raced shutdown disposal */ }
    }
  }

  /// <summary>
  /// The local output a Cast fallback lands on: the system default, else the first device.
  /// One rule for both fallbacks (startup and lost-connection).
  /// </summary>
  private async Task<AudioDeviceInfo?> PickLocalOutputDeviceAsync(CancellationToken ct)
  {
    // Re-enumerated every time: a fallback can fire long after startup, when the device
    // list may differ from the startup snapshot.
    var devices = await _deviceManager.GetOutputDevicesAsync(ct).ConfigureAwait(false);
    return devices.FirstOrDefault(d => d.IsDefault) ?? devices.FirstOrDefault();
  }

  // --- AUD-37: reconnect to a dropped Cast speaker when it returns -----------------------------

  // Guards _reconnectCts, CastReconnectTask and _reconnectEpisode, so a newer drop's watcher
  // replaces the older one and there is never more than one.
  private readonly object _reconnectGate = new();
  private CancellationTokenSource? _reconnectCts;
  private TimeProvider? _reconnectTimeProvider;

  // AUD-37 (review H1). The reconnect episode for the device the last watcher served: when its
  // window began, the backoff a continuing watcher starts at, and when a watcher last made a
  // reconnect. A drop within the stability period of a watcher-made reconnect continues this
  // episode instead of starting a fresh window, so a speaker that accepts the session and then
  // dies is given up on when the ORIGINAL window ends. Cleared by an explicit user action
  // (CancelCastReconnectAsync); a reconnect that outlives the stability period simply stops
  // qualifying, so the next drop starts afresh. Guarded by _reconnectGate.
  private CastReconnectEpisode? _reconnectEpisode;

  // AUD-37 (review H1, slow flapping). When the watchers made each reconnect to each device in
  // the last hour (TimeProvider timestamps, oldest first). A speaker that outlives the stability
  // period and then drops starts a fresh episode every time; this bounds those to
  // AutoReconnectMaxReconnectsPerHour. Cleared by an explicit user action
  // (CancelCastReconnectAsync). Guarded by _reconnectGate.
  private readonly Dictionary<string, Queue<long>> _watcherReconnects = new(StringComparer.Ordinal);

  private sealed class CastReconnectEpisode
  {
    public required string DeviceId { get; init; }
    public required long WindowStart { get; init; }
    public TimeSpan NextDelay { get; set; }
    public long? LastWatcherReconnect { get; set; }

    // Review M3: a watcher of this episode has logged the episode's one failed-attempt Warning.
    public bool FailureWarned { get; set; }
  }

  /// <summary>
  /// The current (or last) reconnect watcher's run, so a test can await its outcome and
  /// shutdown can drain it. Completed with <see cref="CastReconnectOutcome.Cancelled"/> until
  /// the first watcher starts.
  /// </summary>
  internal Task<CastReconnectOutcome> CastReconnectTask { get; private set; } =
    Task.FromResult(CastReconnectOutcome.Cancelled);

  /// <summary>
  /// The clock the reconnect watcher waits on: a <see cref="TimeProvider"/> registered in DI,
  /// else <see cref="TimeProvider.System"/>.
  /// <para>
  /// <b>Test seam (kind C — substitution).</b> Set by <c>AudioEngineInitializationServiceCastReconnectTests</c>
  /// and <c>AudioEngineInitializationServiceCastReconnectHostTests</c> to a fake clock.
  /// <b>Why the real path is unreachable:</b> the backoff runs 5 s to 30 min of real waits, which a
  /// unit test cannot sit through, and sleeping against them races the watcher's own timers.
  /// <b>NOT covered by this seam:</b> nothing beyond the clock — the waits, backoff, window and
  /// stability arithmetic are the real ones, driven by fake time.
  /// </para>
  /// </summary>
  internal TimeProvider ReconnectTimeProvider
  {
    get => _reconnectTimeProvider ??= _serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
    set => _reconnectTimeProvider = value;
  }

  /// <summary>
  /// How long <see cref="CancelCastReconnectAsync"/> waits for a running watcher to finish before
  /// letting the caller proceed regardless. Real time, not <see cref="ReconnectTimeProvider"/>:
  /// it bounds a user's request. Test-only setter (kind A — a bound a test may shorten); production
  /// uses 3 s.
  /// </summary>
  internal TimeSpan CastReconnectCancelBound { get; set; } = TimeSpan.FromSeconds(3);

  /// <summary>
  /// Replaces the probe/connect/switch implementation the watcher drives. Null in production,
  /// where the watcher gets <see cref="ServiceCastReconnectHost"/>.
  /// <para>
  /// <b>Test seam (kind C — substitution).</b> Set by <c>AudioEngineInitializationServiceCastReconnectTests</c>
  /// and <c>AudioEngineInitializationServiceCastReconnectHostTests.Recovery_HandsTheWatcherTheEpochOfItsOwnSwitch</c>.
  /// <b>Why the real path is unreachable:</b> a reconnect needs a Cast receiver to answer a TCP
  /// probe and complete a Cast handshake and receiver launch; no fake socket does.
  /// <b>NOT covered by this seam:</b> the success path of <c>ServiceCastReconnectHost.ConnectAndStartAsync</c>
  /// (the receiver launch and its HTTP-stream wiring) — only a box UAT covers those. Its epoch
  /// checks, idle check, probe, ownership-conditional tear-down, busy-receiver stand-down and
  /// local restore are tested directly through <see cref="CreateProductionCastReconnectHost"/>.
  /// </para>
  /// </summary>
  internal ICastReconnectHost? CastReconnectHostOverride { get; set; }

  /// <summary>
  /// The port the reconnect probe tries first, as <c>GoogleCastOutput.FindReachablePortAsync</c>
  /// does. Production: the standard Cast port, 8009.
  /// <para>
  /// <b>Test seam (kind C — substitution).</b> Set by
  /// <c>AudioEngineInitializationServiceCastReconnectHostTests.Probe_AnsweringPort_ReturnsTheDevice_ClosedPort_ReturnsNull</c>
  /// to a loopback port known to be closed. <b>Why the real path is unreachable:</b> a loopback
  /// test cannot know whether something on the test machine listens on 8009, and if something
  /// does, a "closed" device would be reported reachable. <b>NOT covered by this seam:</b> nothing
  /// — the probe order and the TCP connects are the real ones; only the first port number differs.
  /// </para>
  /// </summary>
  internal int CastProbeStandardPort { get; set; } = 8009;

  /// <summary>
  /// Replaces the read of the receiver's running applications that the reconnect makes before it
  /// launches ours (review M5). Null in production, where <c>GoogleCastOutput.GetRunningApplicationIdsAsync</c>
  /// asks the device.
  /// <para>
  /// <b>Test seam (kind C — substitution).</b> Set by <c>AudioEngineInitializationServiceCastReconnectHostTests</c>.
  /// <b>Why the real path is unreachable:</b> no fake socket can answer a Cast GET_STATUS.
  /// <b>NOT covered by this seam:</b> SharpCaster's status parsing and
  /// <c>GetRunningApplicationIdsAsync</c> itself. The connect before it, the free/busy decision on
  /// its result and the tear-down after it are real.
  /// </para>
  /// </summary>
  internal Func<GoogleCastOutput, CancellationToken, Task<IReadOnlyList<string>>>? ReceiverApplicationsReadOverride { get; set; }

  /// <summary>
  /// The production host, exposed so its engine-facing checks can be tested directly. Test-only
  /// (kind A — visibility): production constructs the same object in <see cref="StartCastReconnectWatcher"/>.
  /// </summary>
  internal ICastReconnectHost CreateProductionCastReconnectHost() => new ServiceCastReconnectHost(this);

  /// <inheritdoc />
  public async Task CancelCastReconnectAsync()
  {
    CancellationTokenSource? cts;
    Task<CastReconnectOutcome> run;
    lock (_reconnectGate)
    {
      // An explicit user action: whatever drops next starts a fresh window, uncapped.
      _reconnectEpisode = null;
      _watcherReconnects.Clear();
      cts = _reconnectCts;
      run = CastReconnectTask;
    }

    if (run.IsCompleted)
    {
      return;
    }

    try
    {
      cts?.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // The watcher finished and disposed its source in the meantime.
    }

    try
    {
      // Bounded: a watcher inside a SharpCaster connect does not observe cancellation. The
      // caller proceeds regardless; the watcher then removes only a connection it made itself.
      await Task.WhenAny(run, Task.Delay(CastReconnectCancelBound)).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cast reconnect: waiting for the cancelled watcher failed");
    }

    if (!run.IsCompleted)
    {
      _logger.LogDebug(
        "Cast reconnect: watcher still finishing after {Bound} s — proceeding with the user's action",
        CastReconnectCancelBound.TotalSeconds);
    }
  }

  /// <summary>
  /// Starts the one reconnect watcher for <paramref name="device"/>, replacing (cancelling) any
  /// earlier one. The new watcher does not begin until the old one has finished, so two can
  /// never be connecting at once. Does nothing when auto-reconnect is off, the device is
  /// unknown, or the service is stopping. A drop soon after a watcher-made reconnect continues
  /// that episode's window and backoff, and once that window has run out starts no watcher. Nor
  /// does it start one once watchers have reconnected this device
  /// <c>AutoReconnectMaxReconnectsPerHour</c> times within the last hour.
  /// </summary>
  private void StartCastReconnectWatcher(ChromecastDeviceInfo? device, CastRecoveryMark mark)
  {
    var options = _audioOutputOptions.Value.GoogleCast;
    if (!options.AutoReconnect || device == null || (_castOutput == null && CastReconnectHostOverride == null))
    {
      return;
    }

    var schedule = CastReconnectSchedule.From(options);
    var time = ReconnectTimeProvider;

    CancellationTokenSource? previousCts;
    lock (_reconnectGate)
    {
      CancellationTokenSource cts;
      try
      {
        if (_serviceStoppingCts.IsCancellationRequested)
        {
          return;
        }

        cts = CancellationTokenSource.CreateLinkedTokenSource(_serviceStoppingCts.Token);
      }
      catch (ObjectDisposedException)
      {
        return; // Service already stopped.
      }

      var previousRun = CastReconnectTask;
      var now = time.GetTimestamp();

      var perHour = Math.Max(1, options.AutoReconnectMaxReconnectsPerHour);
      var recent = CountRecentWatcherReconnects_Locked(device.Id, now, time);
      if (recent >= perHour)
      {
        // Review H1 (slow flapping): each of these reconnects outlived the stability period,
        // so each drop started a fresh window. Stop here rather than cycle all day.
        _reconnectEpisode = null;
        cts.Dispose();
        _logger.LogInformation(
          "Cast: \"{Name}\" has been reconnected automatically {Count} times in the last hour and has dropped again — giving up on reconnecting; pick Cast again to reconnect",
          device.FriendlyName, recent);
        CastReconnectTask = AfterPreviousRunAsync(previousRun, CastReconnectOutcome.GaveUp);
        return;
      }

      CastReconnectStart start;
      var episode = _reconnectEpisode;
      if (episode != null &&
          string.Equals(episode.DeviceId, device.Id, StringComparison.Ordinal) &&
          episode.LastWatcherReconnect is { } lastReconnect &&
          time.GetElapsedTime(lastReconnect, now) <= schedule.Stability)
      {
        // The speaker took the session and dropped again before it counted as stable: the
        // same episode, not a new one.
        if (time.GetElapsedTime(episode.WindowStart, now) + episode.NextDelay > schedule.Window)
        {
          _reconnectEpisode = null;
          cts.Dispose();
          _logger.LogInformation(
            "Cast: \"{Name}\" keeps dropping soon after reconnecting and its {Window} min reconnect window has run out — giving up on reconnecting; pick Cast again to reconnect",
            device.FriendlyName, (int)schedule.Window.TotalMinutes);
          CastReconnectTask = AfterPreviousRunAsync(previousRun, CastReconnectOutcome.GaveUp);
          return;
        }

        episode.LastWatcherReconnect = null;
        start = new CastReconnectStart(episode.WindowStart, episode.NextDelay, episode.FailureWarned);
      }
      else
      {
        _reconnectEpisode = new CastReconnectEpisode
        {
          DeviceId = device.Id,
          WindowStart = now,
          NextDelay = schedule.InitialDelay
        };
        start = new CastReconnectStart(now, schedule.InitialDelay);
      }

      previousCts = _reconnectCts;
      _reconnectCts = cts;

      var watcher = new CastReconnectWatcher(
        CastReconnectHostOverride ?? new ServiceCastReconnectHost(this),
        device,
        mark,
        schedule,
        time,
        _logger,
        start,
        // Recorded as the connect succeeds, before the switch makes the output Cast: a loss
        // of this connection can be reported from that moment on, and its recovery must find
        // the episode already marked (review H1).
        nextDelay => RecordWatcherReconnect(device, nextDelay),
        () => RecordFailureWarned(device));

      CastReconnectTask = Task.Run(() => RunCastReconnectWatcherAsync(watcher, previousRun, device, cts));
    }

    // Outside the lock: cancelling can run the old watcher's continuations inline.
    try
    {
      previousCts?.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // The old watcher already finished and disposed its source.
    }
  }

  private static async Task<CastReconnectOutcome> AfterPreviousRunAsync(
    Task<CastReconnectOutcome> previousRun, CastReconnectOutcome outcome)
  {
    try
    {
      await previousRun.ConfigureAwait(false);
    }
    catch
    {
      // Its outcome is not this one's concern.
    }

    return outcome;
  }

  private async Task<CastReconnectOutcome> RunCastReconnectWatcherAsync(
    CastReconnectWatcher watcher,
    Task<CastReconnectOutcome> previousRun,
    ChromecastDeviceInfo device,
    CancellationTokenSource cts)
  {
    try
    {
      // Never two: the replaced watcher may be inside a connect that does not observe
      // cancellation (SharpCaster), so wait for it to come out before this one may act.
      try
      {
        await previousRun.ConfigureAwait(false);
      }
      catch
      {
        // Its outcome is not this watcher's concern.
      }

      var outcome = await watcher.RunAsync(cts.Token).ConfigureAwait(false);

      if (outcome == CastReconnectOutcome.LostAgainAfterSwitch && !cts.IsCancellationRequested)
      {
        // Re-run the AUD-84 recovery: the loss of the new connection may have been reported
        // while the output was still local, in which case that recovery declined it and nothing
        // else would unmute the local speakers. If it does switch, it starts the next watcher.
        await RestoreLocalOutputAfterCastLossAsync(device, "the Cast connection was lost again right after reconnecting")
          .ConfigureAwait(false);
      }

      return outcome;
    }
    finally
    {
      lock (_reconnectGate)
      {
        if (ReferenceEquals(_reconnectCts, cts))
        {
          _reconnectCts = null;
        }
      }

      cts.Dispose();
    }
  }

  private void RecordFailureWarned(ChromecastDeviceInfo device)
  {
    lock (_reconnectGate)
    {
      var episode = _reconnectEpisode;
      if (episode != null && string.Equals(episode.DeviceId, device.Id, StringComparison.Ordinal))
      {
        episode.FailureWarned = true;
      }
    }
  }

  /// <summary>
  /// The watcher-made reconnects to <paramref name="deviceId"/> within the hour before
  /// <paramref name="now"/>, dropping older ones. Caller holds <c>_reconnectGate</c>.
  /// </summary>
  private int CountRecentWatcherReconnects_Locked(string deviceId, long now, TimeProvider time)
  {
    if (!_watcherReconnects.TryGetValue(deviceId, out var times))
    {
      return 0;
    }

    while (times.Count > 0 && time.GetElapsedTime(times.Peek(), now) > TimeSpan.FromHours(1))
    {
      times.Dequeue();
    }

    return times.Count;
  }

  private void RecordWatcherReconnect(ChromecastDeviceInfo device, TimeSpan nextDelay)
  {
    lock (_reconnectGate)
    {
      // Recorded whatever the episode's state: the hourly cap counts reconnects, not episodes.
      if (!_watcherReconnects.TryGetValue(device.Id, out var times))
      {
        times = new Queue<long>();
        _watcherReconnects[device.Id] = times;
      }

      times.Enqueue(ReconnectTimeProvider.GetTimestamp());

      var episode = _reconnectEpisode;
      if (episode == null || !string.Equals(episode.DeviceId, device.Id, StringComparison.Ordinal))
      {
        return; // A user action reset the episode meanwhile.
      }

      episode.LastWatcherReconnect = ReconnectTimeProvider.GetTimestamp();
      episode.NextDelay = nextDelay;
    }
  }

  /// <summary>
  /// Receiver applications that mean "nobody is using the speaker": the Backdrop (ambient /
  /// idle screen) app. Anything else that is not our own receiver is another sender's session
  /// (AUD-37, review M5). An idle app this list does not know, on some device model, reads as
  /// busy — the safe direction: the reconnect stands down and the user can pick Cast.
  /// </summary>
  internal static readonly IReadOnlySet<string> CastIdleApplicationIds =
    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "E8C28D3C" };

  /// <summary>
  /// True when <paramref name="runningApplicationIds"/> leaves the receiver free for the
  /// reconnect: nothing running, our own receiver app, or the idle screen.
  /// </summary>
  internal static bool IsCastReceiverFreeForUs(IReadOnlyList<string> runningApplicationIds, string ourApplicationId) =>
    runningApplicationIds.All(id =>
      string.Equals(id, ourApplicationId, StringComparison.OrdinalIgnoreCase) || CastIdleApplicationIds.Contains(id));

  /// <summary>The production <see cref="ICastReconnectHost"/>: the real engine, Cast and HTTP outputs.</summary>
  private sealed class ServiceCastReconnectHost : ICastReconnectHost
  {
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    // Review M5: held until the receiver is confirmed free. Review M3: an automatic attempt, whose
    // failure the watcher reports once per episode; the output logs it at Debug.
    private static readonly CastConnectOptions WatcherConnect = new() { HoldUntilReceiverConfirmed = true, AutomaticAttempt = true };

    private readonly AudioEngineInitializationService _svc;
    private bool _startedHttpOutput;

    // AUD-37 (review M1). The private copy of the device record this host's own connect passed
    // to GoogleCastOutput.ConnectAsync. ConnectAsync publishes exactly that reference as
    // ConnectedDevice, and nobody else holds it, so it identifies "the connection we made".
    private ChromecastDeviceInfo? _ownDevice;

    public ServiceCastReconnectHost(AudioEngineInitializationService svc)
    {
      _svc = svc;
    }

    public bool IsStillOnRecoveryOutput(CastRecoveryMark mark)
    {
      if (mark.Epoch.HasValue && _svc._audioEngine is SoundFlowAudioEngine engine)
      {
        return engine.OutputSelectionEpoch == mark.Epoch.Value;
      }

      return string.Equals(_svc._audioEngine.ActiveOutputId, mark.LocalOutputId, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsCastIdle
    {
      get
      {
        var cast = _svc._castOutput;
        if (cast == null || cast.ConnectedDevice != null)
        {
          return false;
        }

        return cast.State is AudioOutputState.Created or AudioOutputState.Ready
          or AudioOutputState.Stopped or AudioOutputState.Error;
      }
    }

    public bool IsCastStreaming => _svc._castOutput?.State == AudioOutputState.Streaming;

    public string? ActiveOutputId => _svc._audioEngine.ActiveOutputId;

    public async Task<ChromecastDeviceInfo?> ProbeAsync(ChromecastDeviceInfo device, CancellationToken ct)
    {
      // A speaker that rebooted may have come back on a different address; the discovery
      // cache tracks that, the record captured at the drop does not.
      var target = device;
      try
      {
        if (_svc._castOutput != null)
        {
          var cached = await _svc._castOutput.GetCachedDevicesAsync(ct).ConfigureAwait(false);
          target = cached.FirstOrDefault(d => d.Id == device.Id) ?? device;
        }
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _svc._logger.LogDebug(ex, "Cast reconnect: could not read the device cache; probing the last known address");
      }

      // The same ports, in the same order, as GoogleCastOutput.FindReachablePortAsync.
      var standardPort = _svc.CastProbeStandardPort;
      var ports = target.Port == standardPort
        ? new[] { standardPort }
        : new[] { standardPort, target.Port };

      foreach (var port in ports)
      {
        using var tcp = new TcpClient();
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(ProbeTimeout);
        try
        {
          await tcp.ConnectAsync(target.IpAddress, port, probeCts.Token).ConfigureAwait(false);
          return target;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
          // Not answering on this port (refused, unreachable, or timed out).
        }
      }

      return null;
    }

    public async Task ConnectAndStartAsync(ChromecastDeviceInfo device, CancellationToken ct)
    {
      var cast = _svc._castOutput ?? throw new InvalidOperationException("No Cast output");
      var castOptions = _svc._audioOutputOptions.Value.GoogleCast;
      var isDirectChannel = string.Equals(castOptions.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase);

      if (cast.State == AudioOutputState.Created)
      {
        await cast.InitializeAsync(ct).ConfigureAwait(false);
      }

      // Source wiring as the startup restore and DevicesController do it.
      if (isDirectChannel)
      {
        cast.SetAudioEngine(_svc._audioEngine);
      }

      _svc.PushNowPlayingMetadataToCast();

      // A fresh reference: the ownership token for every tear-down this host makes.
      var ours = device with { };
      _ownDevice = ours;

      try
      {
        // Review M5: held until the receiver is known to be free — no remembered volume pushed to
        // it, and no status from it reported to the console, while it may be serving another sender.
        await cast.ConnectAsync(ours, WatcherConnect, ct).ConfigureAwait(false);
      }
      catch
      {
        // A failure after ConnectAsync published our connection (a Connected handler throwing,
        // say) leaves it standing — ours to remove. A failure before it published, or its refusal
        // because another party is mid-connect, left nothing of ours; TearDownCastAsync checks.
        await TearDownCastAsync().ConfigureAwait(false);
        throw;
      }

      // Review M2 (AUD-37 × AUD-85). A superseded connect returns normally — State Ready, nothing
      // published — so "returned" is not "ours". Every step below acts on the PUBLISHED connection
      // (the app read, the confirmation, the start), which may by now be a user's: a Disconnect +
      // Connect made while ours was on the network. Checked here and again before each of those.
      ThrowUnlessStillOurs(cast, ct);

      // Review M5: never take the speaker from someone else. Connecting only opened a channel to
      // the receiver; launching our app (StartAsync) is what would end another sender's session.
      await StandDownIfReceiverInUseAsync(cast, castOptions.ApplicationId, ct).ConfigureAwait(false);

      ThrowUnlessStillOurs(cast, ct);

      // Free: end the hold. The remembered level is still the connection's level, and the start
      // below pushes it after launching our receiver app (GoogleCastOutput.SyncVolumeAfterStartAsync,
      // on the DirectChannel path and on the HttpMp3 path when a stream URL is set).
      cast.ConfirmReceiverAvailable();

      try
      {
        await StartOwnConnectionAsync(cast, ours, isDirectChannel, ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        // Left standing: the watcher keeps it for a Cast pick, or tears it down (review M3).
        throw;
      }
      catch
      {
        // The connection is ours; leaving it published would make the Cast output look
        // owned by someone else and retire the watcher on its next check.
        await TearDownCastAsync().ConfigureAwait(false);
        throw;
      }
    }

    /// <summary>
    /// Wires the HTTP stream (HttpMp3) and starts the Cast output on this host's own connection.
    /// The start itself runs to completion (<see cref="CancellationToken.None"/>): a cancellation
    /// inside it — its 250 ms post-launch delay — would set <c>Error</c> on a connection whose
    /// receiver app is already launched. A cancellation is decided after it returns (review M3).
    /// </summary>
    private async Task StartOwnConnectionAsync(
      GoogleCastOutput cast, ChromecastDeviceInfo ours, bool isDirectChannel, CancellationToken ct)
    {
      if (!isDirectChannel)
      {
        await WireHttpStreamAsync(cast, ours, ct).ConfigureAwait(false);
      }

      ThrowUnlessStillOurs(cast, ct);

      if (cast.State != AudioOutputState.Streaming)
      {
        await cast.StartAsync(automaticAttempt: true, CancellationToken.None).ConfigureAwait(false);
      }

      if (cast.State != AudioOutputState.Streaming)
      {
        throw new InvalidOperationException($"Cast output did not reach Streaming (state {cast.State})");
      }
    }

    /// <summary>
    /// Review M2. Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> is
    /// cancelled, or when the Cast output's published connection is no longer the one this host's
    /// connect made (superseded by a newer connect, or taken down by a disconnect or loss).
    /// </summary>
    private void ThrowUnlessStillOurs(GoogleCastOutput cast, CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();
      if (!OwnsPublishedConnection(cast))
      {
        throw new OperationCanceledException(
          "The reconnect's Cast connection is no longer the published one — another connect or a disconnect took over");
      }
    }

    public async Task<bool> TryKeepForCastChoiceAsync()
    {
      try
      {
        var cast = _svc._castOutput;
        var ours = _ownDevice;
        if (cast == null || ours == null || !OwnsPublishedConnection(cast) ||
            _svc._serviceStoppingCts.IsCancellationRequested ||
            !string.Equals(ActiveOutputId, "google-cast", StringComparison.OrdinalIgnoreCase))
        {
          return false;
        }

        var isDirectChannel = string.Equals(
          _svc._audioOutputOptions.Value.GoogleCast.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase);

        // The user chose Cast; this connection is the only one there is to serve that choice, and
        // a user's own connect launches without a receiver check, so this does not either. The
        // confirmation runs AUD-81's deferred console-mute recall; the start applies the
        // start-time mute as for any start.
        cast.ConfirmReceiverAvailable();
        await StartOwnConnectionAsync(cast, ours, isDirectChannel, CancellationToken.None).ConfigureAwait(false);
        return true;
      }
      catch (Exception ex)
      {
        _svc._logger.LogDebug(ex, "Cast reconnect: could not keep the connection for a Cast pick");
        return false;
      }
    }

    private async Task StandDownIfReceiverInUseAsync(GoogleCastOutput cast, string ourApplicationId, CancellationToken ct)
    {
      IReadOnlyList<string> running;
      try
      {
        var read = _svc.ReceiverApplicationsReadOverride;
        running = read != null
          ? await read(cast, ct).ConfigureAwait(false)
          : await cast.GetRunningApplicationIdsAsync(ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        // Left standing (still held, so nothing has been sent to the speaker): the watcher keeps
        // it for a Cast pick — the user's choice, which launches as any user connect does — or
        // removes it (review M3).
        throw;
      }
      catch (Exception ex)
      {
        // Unknown is never launched on — launching blind is exactly what could end someone's
        // session — but nor is it "in use" (review M4): a speaker still booting, whose GET_STATUS
        // times out, or a connection lost during the read, is the common cause, and ending the
        // episode for good on it gave up on a speaker that was coming back. So it is a failed
        // attempt: our held channel is closed (no Cast message — see DisconnectOwnAsync) and the
        // watcher retries at the backoff cap, under its one-Warning-per-episode rule. Only a
        // positively identified foreign application (below) ends the reconnecting.
        await DisconnectOwnAsync(cast).ConfigureAwait(false);
        throw new InvalidOperationException($"its receiver status could not be read: {ex.Message}", ex);
      }

      if (!IsCastReceiverFreeForUs(running, ourApplicationId))
      {
        await DisconnectOwnAsync(cast).ConfigureAwait(false);
        throw new CastSpeakerInUseException(
          $"running {string.Join(", ", running.Where(id => !string.Equals(id, ourApplicationId, StringComparison.OrdinalIgnoreCase)))}");
      }
    }

    /// <summary>
    /// Closes our channel to a receiver that is busy with someone else's app (or whose status could
    /// not be read) — a disconnect only, no media STOP and no app launch or close, so their session
    /// is untouched. That rests on three facts: this calls <c>GoogleCastOutput.DisconnectAsync</c>,
    /// never <c>StopAsync</c> or the engine's <c>TearDownCastOutputAsync</c>; it runs only before
    /// <c>ConfirmReceiverAvailable</c>, so our connection is still held for receiver confirmation;
    /// and <c>DisconnectAsync</c> skips its AUD-81 console-mute release for a held connection (the
    /// <c>wasHeld</c> check in <c>GoogleCastOutput.DisconnectAsync</c>). That release is the one
    /// place <c>DisconnectAsync</c> sends Cast messages — a receiver-application STOP, then SET_MUTE
    /// false, possibly over a fresh connection — and only for a connection that muted the speaker
    /// for the console. Without it, <c>DisconnectAsync</c> unsubscribes from receiver status and
    /// calls SharpCaster 3.0.0's <c>ChromecastClient.DisconnectAsync</c>, which only cancels its
    /// receive loop and closes the socket.
    /// </summary>
    private async Task DisconnectOwnAsync(GoogleCastOutput cast)
    {
      try
      {
        if (OwnsPublishedConnection(cast))
        {
          using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
          await cast.DisconnectAsync(automaticAttempt: true, cts.Token).ConfigureAwait(false);
        }
      }
      catch (Exception ex)
      {
        _svc._logger.LogDebug(ex, "Cast reconnect: disconnecting from a busy receiver failed");
      }
      finally
      {
        _ownDevice = null;
      }
    }

    /// <summary>
    /// True when the Cast output's published connection is the one this host's connect made and
    /// no connect has claimed the output since. Lock-free reads, in an order that cannot pair our
    /// device with someone else's generation: the generation first, then the device it must go
    /// with, then that nothing newer has claimed. A rival connect that claims the output after
    /// this returns true is not seen — that residual window is why the user-facing actions first
    /// cancel and wait for the watcher (<see cref="CancelCastReconnectAsync"/>).
    /// </summary>
    private bool OwnsPublishedConnection(GoogleCastOutput cast)
    {
      var ours = _ownDevice;
      if (ours == null)
      {
        return false;
      }

      var published = cast.PublishedConnectionGeneration;
      return published >= 0 &&
        ReferenceEquals(cast.ConnectedDevice, ours) &&
        cast.ConnectionGeneration == published;
    }

    private async Task WireHttpStreamAsync(GoogleCastOutput cast, ChromecastDeviceInfo device, CancellationToken ct)
    {
      var http = _svc._httpOutput;
      if (http == null)
      {
        return;
      }

      if (http.State is AudioOutputState.Error or AudioOutputState.Created)
      {
        await http.InitializeAsync(ct).ConfigureAwait(false);
      }

      if (http.State is AudioOutputState.Ready or AudioOutputState.Stopped)
      {
        await http.StartAsync(ct).ConfigureAwait(false);
        _startedHttpOutput = true;
      }

      if (http.State == AudioOutputState.Streaming)
      {
        cast.SetStreamUrl(_svc.GetRoutableStreamUrl(http.Mp3StreamUrl, http.Port, device.IpAddress));
      }
    }

    public async Task<bool> TrySwitchToCastAsync(CastRecoveryMark mark, CancellationToken ct)
    {
      if (_svc._audioEngine is SoundFlowAudioEngine engine)
      {
        return mark.Epoch.HasValue
          ? await engine.SetActiveOutputIfEpochAsync(mark.Epoch.Value, "google-cast", ct).ConfigureAwait(false)
          : await engine.SetActiveOutputIfCurrentAsync(mark.LocalOutputId, "google-cast", ct).ConfigureAwait(false);
      }

      // Any other engine: check-then-switch, with a window between the two.
      if (!IsStillOnRecoveryOutput(mark))
      {
        return false;
      }

      await _svc._audioEngine.SetActiveOutputAsync("google-cast", ct).ConfigureAwait(false);
      return true;
    }

    public async Task TearDownCastAsync()
    {
      try
      {
        var cast = _svc._castOutput;
        if (cast == null || !OwnsPublishedConnection(cast))
        {
          // Nothing of ours is published: our connect failed before publishing, or someone
          // else's connection (or connect) has replaced it. Theirs is not ours to remove.
          _svc._logger.LogDebug("Cast reconnect: no connection of ours to tear down — leaving the Cast output alone");
          return;
        }

        if (_svc._audioEngine is SoundFlowAudioEngine engine)
        {
          // Stop + disconnect, capped at 5 s, never throws.
          await engine.TearDownCastOutputAsync(CancellationToken.None, automaticAttempt: true).ConfigureAwait(false);
        }
        else
        {
          using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
          if (cast.State is AudioOutputState.Streaming or AudioOutputState.Ready)
          {
            await cast.StopAsync(cts.Token).ConfigureAwait(false);
          }

          await cast.DisconnectAsync(automaticAttempt: true, cts.Token).ConfigureAwait(false);
        }

        // HttpMp3 only: stop the HTTP stream this host started, unless an output that uses it
        // has been selected since.
        var active = _svc._audioEngine.ActiveOutputId;
        if (_startedHttpOutput && _svc._httpOutput is { } http &&
            !string.Equals(active, "google-cast", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(active, "http-stream", StringComparison.OrdinalIgnoreCase) &&
            http.State == AudioOutputState.Streaming)
        {
          await http.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _startedHttpOutput = false;
      }
      catch (Exception ex)
      {
        _svc._logger.LogDebug(ex, "Cast reconnect: tear-down after an abandoned reconnect failed");
      }
      finally
      {
        _ownDevice = null;
      }
    }

    public async Task RestoreLocalOutputAsync(CastRecoveryMark mark)
    {
      var engine = _svc._audioEngine;
      try
      {
        var active = engine.ActiveOutputId;
        if (string.Equals(active, "google-cast", StringComparison.OrdinalIgnoreCase))
        {
          return; // The switch went through after all, or someone picked Cast: not local's to fix.
        }

        if (engine is SoundFlowAudioEngine gate)
        {
          // Re-applies the recovery's local output only if it is still the active one — a user's
          // pick of another output set its own mute state and is left alone.
          await gate.SetActiveOutputIfCurrentAsync(mark.LocalOutputId, mark.LocalOutputId, CancellationToken.None)
            .ConfigureAwait(false);
          return;
        }

        if (string.Equals(active, mark.LocalOutputId, StringComparison.OrdinalIgnoreCase))
        {
          await engine.SetActiveOutputAsync(mark.LocalOutputId, CancellationToken.None).ConfigureAwait(false);
        }
      }
      catch (Exception ex)
      {
        _svc._logger.LogWarning(ex, "Cast reconnect: could not restore the local output after a failed switch to Cast; unmuting it directly");
        try
        {
          var active = engine.ActiveOutputId;
          if (!string.Equals(active, "google-cast", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(active, "http-stream", StringComparison.OrdinalIgnoreCase))
          {
            engine.SetLocalOutputMuted(false);
          }
        }
        catch (Exception unmuteEx)
        {
          _svc._logger.LogWarning(unmuteEx, "Cast reconnect: could not unmute the local output");
        }
      }
    }
  }

  /// <summary>
  /// Pushes the active source's now-playing metadata to the Cast output before a connect, as
  /// DevicesController does, so the receiver's first media load carries it.
  /// </summary>
  private void PushNowPlayingMetadataToCast()
  {
    if (_castOutput == null || _audioManager?.ActiveSource is not IPrimaryAudioSource primary)
    {
      return;
    }

    var metadata = primary.Metadata;
    if (metadata == null || metadata.Count == 0)
    {
      return;
    }

    var title = metadata.TryGetValue(StandardMetadataKeys.Title, out var t) ? t as string : null;
    var artist = metadata.TryGetValue(StandardMetadataKeys.Artist, out var a) ? a as string : null;
    var album = metadata.TryGetValue(StandardMetadataKeys.Album, out var al) ? al as string : null;
    var albumArtUrl = metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art) ? art as string : null;

    _castOutput.SetNowPlayingMetadata(title, artist, album, albumArtUrl);
  }

  /// <summary>
  /// Starts the service and initializes the audio engine.
  /// </summary>
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      // Clean up orphaned play history entries from unclean shutdown.
      // Runs before audio engine init to prevent fingerprinting from creating duplicates.
      await CloseOrphanedPlayHistoryEntriesAsync(cancellationToken);

      _logger.LogInformation("Initializing audio engine...");

      // Wire the virtual outputs + config manager into the engine so
      // SetActiveOutputAsync can activate/deactivate them and persist the
      // choice. The engine treats these as optional dependencies; the
      // controller-side activation paths still go through the same gate.
      if (_audioEngine is SoundFlowAudioEngine sfEngine)
      {
        sfEngine.AttachOutputCoordination(_castOutput, _httpOutput, _configManager);
      }

      // Initialize the audio engine
      await _audioEngine.InitializeAsync(cancellationToken);
      
      // Start the audio engine
      await _audioEngine.StartAsync(cancellationToken);
      
      _logger.LogInformation("Audio engine initialized and started successfully");

      // Load persisted device display settings from config store (hidden/visible/friendly names).
      // IOptionsMonitor only loads from appsettings.json; user changes are saved to SQLite.
      if (_deviceManager is SoundFlowDeviceManager sfDeviceManager)
      {
        await sfDeviceManager.LoadDisplaySettingsFromStoreAsync(cancellationToken);
      }

      // Enumerate devices
      var outputDevices = await _deviceManager.GetOutputDevicesAsync(cancellationToken);
      var inputDevices = await _deviceManager.GetInputDevicesAsync(cancellationToken);
      
      _logger.LogInformation("Found {OutputCount} output devices and {InputCount} input devices",
        outputDevices.Count, inputDevices.Count);
      
      // Log device details
      foreach (var device in outputDevices)
      {
        _logger.LogInformation("Output device: {DeviceName} (ID: {DeviceId}, Default: {IsDefault})",
          device.Name, device.Id, device.IsDefault);
      }
      
      foreach (var device in inputDevices)
      {
        _logger.LogInformation("Input device: {DeviceName} (ID: {DeviceId})",
          device.Name, device.Id);
      }
      
      // Apply startup audio preferences (output device, source)
      await ApplyStartupPreferencesAsync(outputDevices, cancellationToken);

      // Initialize AudioManager (restores volume/mute/balance from config store)
      if (_audioManager != null)
      {
        await _audioManager.InitializeAsync(cancellationToken);
      }

      // Activate persisted audio source (after volume is restored so audio starts at correct level)
      await ActivatePersistedSourceAsync(cancellationToken);

      // Pre-warm Bluetooth source if configured (creates source without switching to it)
      if (_bluetoothAutoSwitch != null)
      {
        await _bluetoothAutoSwitch.PreWarmBluetoothAsync(cancellationToken);
      }

      // Enable Bluetooth discoverability on startup if configured
      await EnableBluetoothOnStartupAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize audio engine");
      // Don't throw - allow the application to start even if audio fails
    }
  }

  /// <summary>
  /// Applies user preferences for audio source and output on startup.
  /// If no preferences exist, defaults to Radio source and default output device.
  /// </summary>
  private async Task ApplyStartupPreferencesAsync(
    IReadOnlyList<AudioDeviceInfo> outputDevices,
    CancellationToken cancellationToken)
  {
    try
    {
      var prefs = _audioPreferences.CurrentValue;

      // Try reading persisted output device from the config store first,
      // since IOptionsMonitor reads from appsettings.json which doesn't
      // get updated when the user changes the output device at runtime.
      string? persistedOutput = null;
      if (_configManager != null)
      {
        try
        {
          var storeId = _configManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
          persistedOutput = await _configManager.GetValueAsync<string>(storeId, "AudioPreferences:CurrentOutput", ct: cancellationToken);
          if (!string.IsNullOrEmpty(persistedOutput))
          {
            _logger.LogInformation("Found persisted output device preference: {DeviceId}", persistedOutput);
          }
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Could not read persisted output device preference from config store");
        }
      }

      // Prefer persisted value from config store, fall back to IOptionsMonitor
      var preferredOutputId = !string.IsNullOrEmpty(persistedOutput) ? persistedOutput : prefs.CurrentOutput;

      // Handle virtual outputs (google-cast, http-stream)
      if (preferredOutputId == "google-cast" || preferredOutputId == "http-stream")
      {
        _logger.LogInformation("Restoring {Output} output from startup preferences", preferredOutputId);

        // Single gate call: atomically activates the virtual output, stops the
        // other virtual output, sets local-output mute, and persists the choice.
        // This replaces the previous startup path which only activated outputs
        // and forgot to mute the local sink — that omission caused dual-output
        // (Cast + soundbar) after service restart with persisted "google-cast".
        await _audioEngine.SetActiveOutputAsync(preferredOutputId, cancellationToken);

        // Cast still needs background auto-connect to the saved default device.
        // The gate handles activation; auto-connect remains here since it
        // depends on the persisted default-Cast-device lookup.
        //
        // IMPORTANT: the gate above has just muted the local sink. "google-cast"
        // is only a *desired* output at this point — nothing has connected yet.
        // StartCastAutoConnect owns confirming it, and rolling back to the local
        // output if it cannot be confirmed, so the local sink is never left
        // muted for a Cast device that never arrives.
        if (preferredOutputId == "google-cast")
        {
          CastAutoConnectTask = StartCastAutoConnect(cancellationToken);
        }
      }
      else
      {
        // Physical output device
        string? outputToUse = null;
        string? preferredDeviceName = null;
        if (!string.IsNullOrEmpty(preferredOutputId))
        {
          var preferredOutput = outputDevices.FirstOrDefault(d => d.Id == preferredOutputId);
          if (preferredOutput != null)
          {
            outputToUse = preferredOutput.Id;
            preferredDeviceName = preferredOutput.Name;
            _logger.LogInformation("Using preferred output device: {DeviceName}", preferredOutput.Name);
          }
          else
          {
            _logger.LogWarning("Preferred output device {OutputId} not found, using default", preferredOutputId);
          }
        }

        if (outputToUse == null)
        {
          var defaultOutput = outputDevices.FirstOrDefault(d => d.IsDefault);
          if (defaultOutput != null)
          {
            outputToUse = defaultOutput.Id;
            preferredDeviceName = defaultOutput.Name;
            _logger.LogInformation("Using default output device: {DeviceName}", defaultOutput.Name);
          }
        }

        if (outputToUse != null)
        {
          try
          {
            await _deviceManager.SetOutputDeviceAsync(outputToUse, cancellationToken);
            _logger.LogInformation("Output device preference recorded: {DeviceId}", outputToUse);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to record output device preference");
          }

          // AUD-7. Recording the preference is not the same as acting on it, and until now startup
          // did only the former: SetOutputDeviceAsync validates an id against a cache, assigns a
          // string and persists. SwitchPlaybackDevice is the only code anywhere that stops,
          // disposes and re-initialises the native playback device, and its single caller was the
          // interactive HTTP path. So on every restart where the persisted preference was not the
          // device the engine happened to initialise, the UI reported one output while audio came
          // out of another — deterministically, not intermittently.
          //
          // Synchronous here, unlike the controller's fire-and-forget: the controller defers it to
          // avoid disrupting the HTTP socket mid-response, and startup has no response to protect.
          // Doing it inline also means nothing can start playing before the device is right.
          try
          {
            int deviceIndex = _audioEngine.GetDeviceIndexById(outputToUse);
            if (deviceIndex < 0)
            {
              // Includes the pre-AUD-6 ordinal case, which resolves to -1 by design so the box
              // falls back to the system default rather than trusting a key whose meaning is lost.
              _logger.LogInformation(
                "Output preference {DeviceId} did not resolve to a local playback device; " +
                "leaving the engine on its initialised device.", outputToUse);
            }
            else if (_audioEngine.SwitchPlaybackDevice(deviceIndex))
            {
              _logger.LogInformation(
                "Native playback device now matches the preference: {DeviceName} (index {Index})",
                preferredDeviceName ?? outputToUse, deviceIndex);
            }
            else
            {
              _logger.LogWarning(
                "Native switch to {DeviceId} (index {Index}) failed; audio may not be on the " +
                "reported output.", outputToUse, deviceIndex);
            }
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to switch the native playback device to {DeviceId}", outputToUse);
          }

          // Verify the output device actually connected to the correct PipeWire node.
          // After PipeWire restarts, MiniAudio device indices may shift, causing the
          // wrong device to be selected even though the ID matches.
          try
          {
            var currentDevices = await _deviceManager.GetOutputDevicesAsync(cancellationToken);
            var selectedId = _deviceManager.GetSelectedOutputDeviceId();
            var activeDevice = selectedId != null
              ? currentDevices.FirstOrDefault(d => d.Id == selectedId)
              : null;
            if (activeDevice != null && preferredDeviceName != null &&
                !activeDevice.Name.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase))
            {
              _logger.LogWarning(
                "Output device mismatch: expected \"{Expected}\" but connected to \"{Actual}\" — searching by name",
                preferredDeviceName, activeDevice.Name);

              var correctDevice = currentDevices.FirstOrDefault(d =>
                d.Name.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase));
              if (correctDevice != null)
              {
                _logger.LogInformation("Found correct device \"{Name}\" at ID {Id} — switching",
                  correctDevice.Name, correctDevice.Id);
                await _deviceManager.SetOutputDeviceAsync(correctDevice.Id, cancellationToken);
                outputToUse = correctDevice.Id;
              }
              else
              {
                _logger.LogWarning("Could not find device matching \"{Name}\" — using current device",
                  preferredDeviceName);
              }
            }
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Output device verification failed — continuing with current device");
          }

          // Notify the gate that local is the active output. This stops any
          // lingering virtual outputs, unmutes the local sink, and persists
          // AudioPreferences:CurrentOutput so the next restart picks the same device.
          try
          {
            await _audioEngine.SetActiveOutputAsync(outputToUse, cancellationToken);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to set active output via gate for local device {DeviceId}", outputToUse);
          }
        }
      }

      _logger.LogInformation("Audio startup output configuration applied");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to apply startup preferences");
    }
  }

  /// <summary>
  /// Activates the last-used audio source on startup. Falls back to Radio if no preference exists.
  /// Must be called AFTER AudioManager.InitializeAsync() so volume is restored before audio starts.
  /// </summary>
  private async Task ActivatePersistedSourceAsync(CancellationToken cancellationToken)
  {
    if (_audioManager == null)
    {
      _logger.LogDebug("AudioManager not available, skipping source activation on startup");
      return;
    }

    try
    {
      var prefs = _audioPreferences.CurrentValue;
      var sourceToActivate = !string.IsNullOrEmpty(prefs.CurrentSource)
        ? prefs.CurrentSource
        : "Radio";

      if (!Enum.TryParse<AudioSourceType>(sourceToActivate, true, out var sourceType))
      {
        _logger.LogWarning("Invalid persisted source type '{Source}', falling back to Radio", sourceToActivate);
        sourceType = AudioSourceType.Radio;
      }

      _logger.LogInformation("Activating persisted source: {SourceType} (from {Origin})",
        sourceType,
        !string.IsNullOrEmpty(prefs.CurrentSource) ? "preferences" : "default");

      await _audioManager.GetOrCreateSourceAsync(sourceType, switchToSource: true, cancellationToken);
      _logger.LogInformation("Source {SourceType} activated on startup", sourceType);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to activate persisted source, UI will handle default selection");
    }
  }

  /// <summary>
  /// Enables Bluetooth discoverability on startup if configured.
  /// On Windows, the adapter will start but A2DP sink (acting as a speaker)
  /// is not natively supported — phones can see the device but cannot stream audio.
  /// This works on the target Linux/Raspberry Pi platform where BlueZ supports A2DP sink.
  /// </summary>
  private async Task EnableBluetoothOnStartupAsync(CancellationToken cancellationToken)
  {
    try
    {
      var opts = _bluetoothOptions.Value;
      if (!opts.Enabled || !opts.EnableOnStartup)
      {
        _logger.LogDebug("Bluetooth auto-start disabled (Enabled={Enabled}, EnableOnStartup={EnableOnStartup})",
          opts.Enabled, opts.EnableOnStartup);
        return;
      }

      if (_bluetoothService == null)
      {
        _logger.LogDebug("Bluetooth service not available, skipping auto-start");
        return;
      }

      var deviceName = opts.DeviceName;
      _logger.LogInformation("Enabling Bluetooth on startup as '{DeviceName}'...", deviceName);
      var success = await _bluetoothService.StartAsync(deviceName, cancellationToken);

      if (success)
      {
        _logger.LogInformation("Bluetooth started successfully, device is discoverable as '{DeviceName}'", deviceName);
      }
      else
      {
        _logger.LogWarning("Bluetooth StartAsync returned false — adapter may not be available");
      }
    }
    catch (Exception ex)
    {
      // Bluetooth failure must not block application startup
      _logger.LogWarning(ex, "Failed to enable Bluetooth on startup — continuing without Bluetooth");
    }
  }

  /// <summary>
  /// Starts background auto-connect to the saved default Cast device and, either
  /// way, guarantees the local output does not stay muted for a Cast device that
  /// never becomes usable.
  ///
  /// The gate (<see cref="IAudioEngine.SetActiveOutputAsync"/>) has already muted
  /// the local sink by the time this runs — that mute is correct and deliberate
  /// (it is what keeps Cast and the soundbar from playing simultaneously), but it
  /// nominates an output rather than confirming one. Every path out of this method
  /// therefore either ends with Cast actually <c>Streaming</c> or rolls back to the
  /// local output. The rollback goes through the gate specifically so it also
  /// rewrites the persisted <c>AudioPreferences:CurrentOutput</c> — otherwise the
  /// same inconsistent preference replays the failure on every subsequent restart.
  /// </summary>
  /// <returns>The background confirm-or-roll-back task.</returns>
  private Task StartCastAutoConnect(CancellationToken cancellationToken)
  {
    var castOptions = _audioOutputOptions.Value.GoogleCast;
    var isDirectChannel = string.Equals(castOptions.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase);

    // In DirectChannel mode, wire the audio engine so GoogleCastOutput can
    // create a stream reader for sending PCM data over the Cast message bus.
    if (isDirectChannel && _castOutput != null)
    {
      _castOutput.SetAudioEngine(_audioEngine);
      _logger.LogInformation("DirectChannel mode: audio engine wired to Cast output");
    }

    var timeout = CastConnectTimeoutOverride
      ?? TimeSpan.FromSeconds(Math.Max(1, castOptions.StartupConnectTimeoutSeconds));

    // Scheduled with CancellationToken.None and cancelled from inside instead, so
    // the retained task always runs to completion rather than surfacing as a bare
    // cancelled task to whoever awaits it.
    return Task.Run(
      () => RunCastAutoConnectAsync(isDirectChannel, castOptions.StreamingMode, timeout, cancellationToken),
      CancellationToken.None);
  }

  /// <summary>
  /// Attempts the Cast auto-connect, then independently watchdogs the result.
  /// The attempt and the watchdog run concurrently so the watchdog also covers
  /// bail-outs the attempt does not explicitly handle.
  /// </summary>
  private async Task RunCastAutoConnectAsync(
    bool isDirectChannel,
    string streamingMode,
    TimeSpan timeout,
    CancellationToken cancellationToken)
  {
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken, _serviceStoppingCts.Token, _castResolvedCts.Token);
    var ct = linkedCts.Token;

    await Task.WhenAll(
      TryConnectCastAsync(isDirectChannel, streamingMode, ct),
      WatchdogCastStreamingAsync(timeout, ct)).ConfigureAwait(false);
  }

  /// <summary>
  /// The auto-connect attempt. Every early return falls back to local first —
  /// these are the paths that previously logged and left the local sink muted.
  /// </summary>
  private async Task TryConnectCastAsync(bool isDirectChannel, string streamingMode, CancellationToken ct)
  {
    try
    {
      var castDeviceId = await ResolveDefaultCastDeviceIdAsync(ct).ConfigureAwait(false);

      // Bail-out 1: no default Cast device configured, or no Cast output at all.
      if (string.IsNullOrEmpty(castDeviceId) || _castOutput == null)
      {
        _logger.LogWarning(
          "Persisted output is google-cast but there is nothing to connect to " +
          "(defaultCastDeviceConfigured={HasDevice}, castOutputAvailable={HasOutput})",
          !string.IsNullOrEmpty(castDeviceId), _castOutput != null);
        await FallBackToLocalOutputAsync("no default Cast device configured", ct).ConfigureAwait(false);
        return;
      }

      _logger.LogInformation("Auto-connecting to default Cast device on startup: {Id}", castDeviceId);

      // Capture the output-selection epoch BEFORE any connect work. The connect
      // runs outside the engine's output lock (it is far too long to hold it),
      // so this token is what proves at the end that nothing reselected the
      // output while we were on the network.
      var castEpoch = _audioEngine is SoundFlowAudioEngine epochEngine
        ? await epochEngine.BeginCastConnectAsync(ct).ConfigureAwait(false)
        : (int?)null;

      // Give Cast discovery a moment to populate the cache.
      await Task.Delay(CastDiscoverySettleDelay, ct).ConfigureAwait(false);

      var cached = await _castOutput.GetCachedDevicesAsync(ct).ConfigureAwait(false);
      var device = cached.FirstOrDefault(d => d.Id == castDeviceId);

      // Bail-out 2: the saved device isn't on the network.
      if (device == null)
      {
        _logger.LogWarning("Default Cast device {Id} not found in cache after startup, skipping auto-connect",
          castDeviceId);
        await FallBackToLocalOutputAsync("default Cast device not discovered", ct).ConfigureAwait(false);
        return;
      }

      if (_castOutput.State == AudioOutputState.Created)
      {
        await _castOutput.InitializeAsync(ct).ConfigureAwait(false);
      }

      await _castOutput.ConnectAsync(device, ct).ConfigureAwait(false);

      // Wire the HTTP audio stream (HttpMp3 mode only)
      if (!isDirectChannel && _httpOutput?.State == AudioOutputState.Streaming)
      {
        var streamUrl = GetRoutableStreamUrl(_httpOutput.Mp3StreamUrl, _httpOutput.Port, device.IpAddress);
        _castOutput.SetStreamUrl(streamUrl);
      }

      await _castOutput.StartAsync(ct).ConfigureAwait(false);

      // Late-success guard, now decided by the engine under its output lock.
      // Several calls in the connect chain above do not observe cancellation at
      // all (the SharpCaster-facing ConnectChromecast / LaunchApplicationAsync /
      // volume-sync calls), so the rollback can have fired and unmuted the local
      // sink while we were parked inside one of them. Finishing the connect at
      // that point would leave Cast streaming AND local unmuted — the dual-output
      // bug the startup mute exists to prevent.
      //
      // Deliberately NOT a check of _localFallbackApplied here: reading a flag
      // and then tearing down are two steps with a window between them, and the
      // output can move inside that window. TryCommitCastConnectAsync does the
      // check and the teardown under one acquisition of the engine's lock.
      if (castEpoch.HasValue && _audioEngine is SoundFlowAudioEngine commitEngine &&
          !await commitEngine.TryCommitCastConnectAsync(castEpoch.Value, CancellationToken.None).ConfigureAwait(false))
      {
        return;
      }

      _logger.LogInformation("Startup: Auto-connected to Cast device: {Name} (mode: {Mode})",
        device.FriendlyName, streamingMode);

      // Deliberately no "success" short-circuit beyond that: GoogleCastOutput
      // .StartAsync returns having set state to Ready (not Streaming) when no
      // receiver is actually attached. The watchdog distinguishes the two.
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      // Host is shutting down — StopAsync owns tear-down. Do not rewrite the
      // user's persisted output preference on the way out.
    }
    catch (Exception ex)
    {
      // Bail-out 3: anything else thrown mid-connect.
      _logger.LogWarning(ex, "Failed to auto-connect to Cast device on startup");
      await FallBackToLocalOutputAsync("Cast auto-connect failed", ct).ConfigureAwait(false);
    }
  }

  /// <summary>
  /// The durable guard: if Cast has not actually reached
  /// <see cref="AudioOutputState.Streaming"/> within the timeout, roll back to the
  /// local output. This covers the explicit bail-outs above plus any future path
  /// that leaves Cast nominated-but-not-working.
  /// </summary>
  private async Task WatchdogCastStreamingAsync(TimeSpan timeout, CancellationToken ct)
  {
    try
    {
      await Task.Delay(timeout, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return;
    }

    // Already rolled back by one of the explicit bail-outs.
    if (Volatile.Read(ref _localFallbackApplied))
    {
      return;
    }

    // Cast is genuinely working — the startup mute is correct, leave it alone.
    if (_castOutput?.State == AudioOutputState.Streaming)
    {
      _logger.LogInformation("Cast output confirmed streaming — local output stays muted");
      return;
    }

    _logger.LogWarning(
      "Cast output did not reach Streaming within {Timeout}s (state={State}) — rolling back to local output",
      timeout.TotalSeconds, _castOutput?.State.ToString() ?? "<no cast output>");

    await FallBackToLocalOutputAsync("Cast not streaming before timeout", ct).ConfigureAwait(false);
  }

  /// <summary>
  /// Resolves the default Cast device id, preferring the SQLite config store.
  ///
  /// <c>DevicesController</c> writes <c>AudioPreferences:DefaultCastDeviceId</c>
  /// to the config store when the user sets a default Cast device, but
  /// IOptionsMonitor only ever reflects appsettings.json — the same asymmetry
  /// already called out for CurrentOutput in <see cref="ApplyStartupPreferencesAsync"/>.
  /// Reading only IOptionsMonitor made a perfectly valid saved device invisible at
  /// startup, which widened the window where Cast was nominated but unreachable.
  /// </summary>
  private async Task<string?> ResolveDefaultCastDeviceIdAsync(CancellationToken cancellationToken)
  {
    if (_configManager != null)
    {
      try
      {
        var storeId = _configManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
        var persisted = await _configManager.GetValueAsync<string>(
          storeId, "AudioPreferences:DefaultCastDeviceId", ct: cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(persisted))
        {
          _logger.LogInformation("Found persisted default Cast device: {DeviceId}", persisted);
          return persisted;
        }
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Could not read persisted default Cast device from config store");
      }
    }

    var fromAppSettings = _audioPreferences.CurrentValue.DefaultCastDeviceId;
    return string.IsNullOrEmpty(fromAppSettings) ? null : fromAppSettings;
  }

  /// <summary>
  /// Rolls the active output back to a local device, unmuting the local sink and
  /// persisting the corrected preference. Idempotent on success; a transient
  /// failure (e.g. no devices enumerated yet) leaves the door open for the
  /// watchdog to retry.
  /// </summary>
  private async Task FallBackToLocalOutputAsync(string reason, CancellationToken cancellationToken)
  {
    try
    {
      await _fallbackLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    catch (ObjectDisposedException)
    {
      // Raced shutdown disposal. SemaphoreSlim.WaitAsync checks disposal before
      // it checks the token, so a cancelled token does not pre-empt this.
      return;
    }

    bool resolved;
    try
    {
      resolved = await ApplyLocalFallbackAsync(reason, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _fallbackLock.Release();
    }

    // Signalled outside the lock: cancelling runs continuations synchronously,
    // and doing that while holding the semaphore invites surprises.
    if (resolved)
    {
      // Retire the watchdog and abandon any in-flight Cast connect.
      try
      {
        await _castResolvedCts.CancelAsync().ConfigureAwait(false);
      }
      catch (ObjectDisposedException)
      {
        // Raced service shutdown; nothing left to signal.
      }
    }
  }

  /// <summary>
  /// The guarded body of the rollback. Runs under <c>_fallbackLock</c>.
  /// </summary>
  /// <returns>
  /// True when the output question is settled and no further rollback should be
  /// attempted; false when nothing was done and a later attempt may still help.
  /// </returns>
  private async Task<bool> ApplyLocalFallbackAsync(string reason, CancellationToken cancellationToken)
  {
    try
    {
      if (_localFallbackApplied)
      {
        return false;
      }

      // Don't stomp a newer choice: by watchdog time the user may have picked a
      // different output through the UI. A null ActiveOutputId means the engine
      // never reported one, so treat it as "still ours" and proceed — failing
      // open here is what keeps the local sink from staying muted.
      var active = _audioEngine.ActiveOutputId;
      if (!string.IsNullOrEmpty(active) &&
          !string.Equals(active, "google-cast", StringComparison.OrdinalIgnoreCase))
      {
        _logger.LogInformation(
          "Skipping local-output fallback ({Reason}): active output is now {ActiveOutput}", reason, active);
        Volatile.Write(ref _localFallbackApplied, true);
        return true;
      }

      var target = await PickLocalOutputDeviceAsync(cancellationToken).ConfigureAwait(false);
      if (target == null)
      {
        // Nothing to unmute: with no output device there is no local playback
        // device either. Left retryable on purpose.
        _logger.LogError(
          "Cast output unusable ({Reason}) and no local output device is available — " +
          "cannot restore local audio", reason);
        return false;
      }

      _logger.LogWarning(
        "Falling back to local output \"{DeviceName}\" ({DeviceId}) because Cast could not be confirmed ({Reason})",
        target.Name, target.Id, reason);

      try
      {
        await _deviceManager.SetOutputDeviceAsync(target.Id, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        // Non-fatal: the gate call below still unmutes and persists.
        _logger.LogWarning(ex, "Fallback: could not select local output device {DeviceId}", target.Id);
      }

      // The gate tears down the half-open Cast/HTTP outputs, unmutes the local
      // sink, and persists AudioPreferences:CurrentOutput so the next restart
      // does not replay this failure.
      await _audioEngine.SetActiveOutputAsync(target.Id, cancellationToken).ConfigureAwait(false);
      Volatile.Write(ref _localFallbackApplied, true);

      _logger.LogInformation(
        "Local output \"{DeviceName}\" restored and unmuted after Cast fallback", target.Name);
      return true;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Shutting down — leave state alone.
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to fall back to local output after Cast could not be confirmed");
      return false;
    }
  }

  /// <summary>
  /// Resolves the stream URL to use the local LAN IP (Cast devices need a routable address).
  /// </summary>
  private string GetRoutableStreamUrl(string streamUrl, int port, string? targetDeviceIp)
  {
    try
    {
      var localIp = GetLocalIPAddress(targetDeviceIp);
      if (localIp != null)
      {
        var uri = new Uri(streamUrl);
        return $"http://{localIp}:{port}{uri.PathAndQuery}";
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not resolve routable stream URL");
    }
    return streamUrl;
  }

  /// <summary>
  /// Gets the local LAN IP address, preferring one on the same subnet as the target.
  /// </summary>
  private static string? GetLocalIPAddress(string? targetDeviceIp)
  {
    IPAddress? targetIp = null;
    if (!string.IsNullOrEmpty(targetDeviceIp))
    {
      IPAddress.TryParse(targetDeviceIp, out targetIp);
    }

    string? fallbackIp = null;
    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
    {
      if (ni.OperationalStatus != OperationalStatus.Up)
      {
        continue;
      }

      if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
      {
        continue;
      }

      var desc = ni.Description.ToLowerInvariant();
      var name = ni.Name.ToLowerInvariant();
      if (desc.Contains("hyper-v") || desc.Contains("virtual") ||
          name.Contains("vethernet") || name.Contains("wsl") ||
          name.Contains("docker") || name.Contains("br-"))
      {
        continue;
      }

      foreach (var addr in ni.GetIPProperties().UnicastAddresses)
      {
        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
        {
          continue;
        }

        if (IPAddress.IsLoopback(addr.Address))
        {
          continue;
        }

        if (targetIp != null && addr.IPv4Mask != null)
        {
          var localBytes = addr.Address.GetAddressBytes();
          var maskBytes = addr.IPv4Mask.GetAddressBytes();
          var targetBytes = targetIp.GetAddressBytes();
          bool sameSubnet = true;
          for (int i = 0; i < 4; i++)
          {
            if ((localBytes[i] & maskBytes[i]) != (targetBytes[i] & maskBytes[i]))
            { sameSubnet = false; break; }
          }
          if (sameSubnet)
          {
            return addr.Address.ToString();
          }
        }

        fallbackIp ??= addr.Address.ToString();
      }
    }
    return fallbackIp;
  }

  /// <summary>
  /// Closes orphaned play history entries left by unclean shutdown.
  /// Must run before audio engine initialization so fingerprinting doesn't create duplicates.
  /// </summary>
  private async Task CloseOrphanedPlayHistoryEntriesAsync(CancellationToken cancellationToken)
  {
    try
    {
      using var scope = _serviceProvider.CreateScope();
      var repo = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (repo != null)
      {
        var closed = await repo.CloseOrphanedEntriesAsync(TimeSpan.FromMinutes(2), cancellationToken);
        if (closed > 0)
        {
          _logger.LogInformation("Cleaned up {Count} orphaned play history entries from previous shutdown", closed);
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to clean up orphaned play history entries");
    }
  }

  /// <summary>
  /// Stops the service and gracefully shuts down the audio engine.
  /// </summary>
  public async Task StopAsync(CancellationToken cancellationToken)
  {
    try
    {
      _logger.LogInformation("Stopping audio engine...");

      if (_castOutput != null)
      {
        _castOutput.Disconnected -= OnCastOutputDisconnected;
      }

      // Stop the Cast confirm-or-roll-back work before tearing anything down, so
      // its watchdog can't fire a fallback (and a config write) mid-shutdown.
      // Bounded drain: this work is best-effort and must not delay shutdown.
      try
      {
        await _serviceStoppingCts.CancelAsync();
      }
      catch (ObjectDisposedException)
      {
        // StopAsync called twice — already torn down.
      }

      var castTask = CastAutoConnectTask;
      if (castTask != null)
      {
        await Task.WhenAny(castTask, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
      }

      // AUD-37: the reconnect watcher is linked to _serviceStoppingCts, so it is already
      // cancelled; a wait ends at once. A connect in flight may not observe cancellation, so
      // the drain is bounded the same way.
      await Task.WhenAny(CastReconnectTask, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));

      // Graceful Cast shutdown: stop our streaming/media and close our
      // connection to the receiver. No receiver-application stop is sent
      // unless this connection muted the speaker for a muted console (AUD-81):
      // then our app is stopped and the speaker unmuted, possibly over a fresh
      // connection — see TearDownCastOutputAsync. Single
      // source of truth: the same TearDownCastOutputAsync that the
      // SetActiveOutputAsync gate uses when transitioning away from Cast.
      // Best-effort; never blocks engine stop (5s internal cap + try/catch).
      if (_audioEngine is SoundFlowAudioEngine sfEngine)
      {
        await sfEngine.TearDownCastOutputAsync(cancellationToken);
      }

      if (_audioEngine.State == Radio.Core.Interfaces.Audio.AudioEngineState.Running)
      {
        await _audioEngine.StopAsync(cancellationToken);
      }

      _logger.LogInformation("Audio engine stopped successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping audio engine");
    }
    finally
    {
      // Dispose only once the background work has actually finished — it holds a
      // linked token source built from these, and the drain above is capped at 1s.
      // If it is still running the process is exiting anyway, so leaving them to
      // the process teardown is strictly safer than disposing underneath it.
      var pending = CastAutoConnectTask;
      if ((pending == null || pending.IsCompleted) && CastReconnectTask.IsCompleted)
      {
        _serviceStoppingCts.Dispose();
        _castResolvedCts.Dispose();
        _fallbackLock.Dispose();
      }
      else
      {
        _logger.LogDebug(
          "Cast auto-connect or reconnect task still running at shutdown — deferring synchronisation-primitive disposal");
      }
    }
  }
}
