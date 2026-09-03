using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Reads rotary encoder events from a RotaryUsb device (VID 0xCAFE / PID 0x4005) over USB HID.
///
/// <para>
/// <b>Wire protocol (Input Report 0x01, 36-byte payload).</b> Verified against the live device's HID
/// report descriptor on 2026-09-02 and against <c>RotaryUsb/docs/INTEGRATION.md</c> §4. All values
/// little-endian; offsets are <i>payload</i> offsets, so the buffer index is offset + 1 because the
/// report ID occupies byte 0.
/// </para>
///
/// <list type="table">
///   <item><term>0–15</term><description>int32 × 4 — clamped positions</description></item>
///   <item><term>16</term><description>uint8 — button bitmask, bit <i>n</i> = encoder <i>n</i></description></item>
///   <item><term>17</term><description>uint8 — active acceleration tiers, 2 bits per encoder</description></item>
///   <item><term>18–19</term><description>reserved</description></item>
///   <item><term>20–35</term><description>int32 × 4 — free-running movement accumulators</description></item>
/// </list>
///
/// <para>
/// <b>What the previous implementation got wrong, so it is not reintroduced.</b> It read an 8-byte
/// report and took bytes 1–4 as <c>sbyte</c> deltas and byte 5 as the button mask. The device sends
/// none of that: the report is 37 bytes including the ID, deltas do not appear on the wire at all,
/// and the button mask is at payload 16. Every UX behaviour built on those values was being built on
/// garbage.
/// </para>
///
/// <para>
/// <b>Movement is an odometer, not a delta — and this is the dangerous part.</b> It is a running
/// total since power-on that keeps accruing while nothing is listening. Differencing it against the
/// last value seen <i>before</i> a disconnect would deliver an entire outage as one delta, on the
/// volume knob. So the first report after every connect is a <b>baseline, not an input</b>: recorded
/// and discarded. Designer's test for it, verbatim: <i>"Turn a knob ~50 detents while unplugged,
/// then replug: volume does not jump."</i>
/// </para>
///
/// <para>
/// The accumulator <b>wraps</b> at 32 bits rather than saturating, so the subtraction is
/// <c>unchecked</c> — two's-complement gives the correct signed delta straight across the boundary.
/// A saturating design would have frozen the control after ~119 hours of continuous fast spinning.
/// </para>
///
/// <para>
/// <b>Silence is the idle state, not a fault.</b> The device transmits only when the report contents
/// change, so the read blocks until the next report rather than polling. An idle device is expected
/// to produce nothing for long stretches and must never be treated as a disconnect.
/// </para>
///
/// <para>
/// Movement already includes acceleration (<c>step_size × tier_multiplier</c>) and respects the
/// per-encoder <c>reverse</c> flag, so the host does not reimplement tier logic or direction. Detent
/// density is firmware-owned. ⚠ For a <i>bounded</i> control the device's own clamped
/// <c>position</c> is the better input — see <see cref="GetPosition"/> and the note on
/// <see cref="EncoderTurnedEventArgs"/> consumers in ENC-3/ENC-7.
/// </para>
/// </summary>
public class HidRotaryEncoderService : IRotaryEncoderService, IRotaryEncoderProvisioning
{
  private readonly ILogger<HidRotaryEncoderService> _logger;
  private readonly IOptionsMonitor<RotaryEncoderOptions> _options;
  private readonly RotaryEncoderDesignedConfig _designedConfig;
  private readonly TimeProvider _timeProvider;
  private CancellationTokenSource? _cts;
  private Task? _readTask;
  private bool _isConnected;
  private bool _disposed;

  /// <summary>
  /// Serialises whole configuration operations — the boot push and every owner-initiated command —
  /// so two writers never interleave on one stream and two waiters never contend for the single
  /// read-back slot. Does not guard the read loop's <c>ReadAsync</c>; that has exactly one reader by
  /// construction.
  /// </summary>
  private readonly SemaphoreSlim _maintenanceLock = new(1, 1);

  /// <summary>
  /// The live HID stream, or null while disconnected. Written by the read loop, read by maintenance
  /// commands arriving on request threads.
  ///
  /// <para>
  /// ⚠ It is read without synchronisation, and the reason that is safe is narrow enough to state
  /// rather than assume: the worst a stale non-null reference can do is make a command write to a
  /// stream that is closing or closed, which throws <see cref="IOException"/> or
  /// <see cref="ObjectDisposedException"/>. It cannot corrupt the read loop — that still has exactly
  /// one reader — and it cannot silently succeed. It is <b>not</b> a promise about which error the
  /// caller sees: a throw during a push is classified as a failed push, while a throw during the
  /// flash write surfaces as a failed request.
  /// </para>
  /// </summary>
  private volatile HidStream? _liveStream;

  /// <summary>
  /// Armed by <see cref="ArmConfigReadBack"/> immediately before a <c>0x03/0x04</c> read-config
  /// request, and completed by <see cref="TryClaimConfigReadBack"/> — which the read loop calls for
  /// every report — when the device answers. Null when no read-back is outstanding.
  ///
  /// <para>
  /// <b>One slot, and the wire carries no correlation id</b>, so a reply is matched to whoever is
  /// waiting rather than to the request that produced it. The bound on that is narrow and worth
  /// stating rather than assuming: <see cref="_maintenanceLock"/> serialises whole operations, and
  /// within one <see cref="ApplyConfigurationAsync"/> retry loop every attempt re-sends the same
  /// <c>desired</c> bytes, so even a late reply is a reply about the configuration being verified.
  /// The residual window is a reply arriving after its own timeout has expired <i>and</i> after a
  /// different operation has armed a new waiter — it would then be compared against the wrong
  /// <c>desired</c>. On a directly attached USB HID device whose round-trip is single-digit
  /// milliseconds against a 2 s timeout that is not reachable in practice, and it self-corrects on
  /// the next operation, so it is recorded rather than fixed: closing it properly needs a nonce in
  /// the request echoed back in report <c>0x02</c>, which is a firmware protocol change. Logged in
  /// <c>design/FUTURE-WORK.md</c>.
  /// </para>
  /// </summary>
  private TaskCompletionSource<RotaryEncoderDeviceConfig>? _pendingConfigRead;

  /// <summary>Guards the retained snapshot fields, which a request thread reads while the read loop writes.</summary>
  private readonly object _snapshotGate = new();
  private RotaryEncoderDeviceConfig? _lastPushed;
  private RotaryEncoderDeviceConfig? _lastReadBack;
  private DateTimeOffset? _lastVerifiedUtc;
  private DateTimeOffset? _lastAttemptedUtc;
  private DateTimeOffset? _lastSavedToDeviceUtc;
  private RotaryEncoderFlashState _flashState = RotaryEncoderFlashState.NeverSaved;

  // The wire protocol lives in RotaryEncoderDecoder so it can be tested without hardware.
  private readonly RotaryEncoderDecoder _decoder = new();

  /// <summary>
  /// True once the device has been opened at least once in this process. ENC-0's notification policy
  /// is asymmetric — absent-at-boot and dropped-mid-session are the same <c>IsConnected=false</c> and
  /// are not the same event — so the transition has to carry this.
  /// </summary>
  private bool _everConnected;

  // Guards the absent-device log so it fires once per absence rather than once per rescan.
  private bool _announcedAbsence;

  // Same, for the permissions case — which is permanent until someone changes the system, so it
  // would otherwise log a warning and a stack trace every couple of seconds indefinitely.
  private bool _announcedUnauthorized;

  private RotaryEncoderConfigStatus _configStatus = RotaryEncoderConfigStatus.Unknown;

  /// <summary>
  /// Makes the compare-and-set in <see cref="ConfigStatus"/>'s setter atomic against the two threads
  /// that write it.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Every write inside <see cref="ApplyConfigurationAsync"/> is serialised by
  /// <see cref="_maintenanceLock"/>. The write in <see cref="RaiseConnectionChanged"/> is not — it runs
  /// on the <b>read-loop thread</b>, while a <i>Re-apply</i> or <i>Save to device</i> from the Settings
  /// page is running concurrently on an <b>ASP.NET request thread</b> that holds that lock. The setter
  /// is a check-then-act, so without this gate a <i>Re-apply</i> completing as the USB lead is knocked
  /// loose can interleave with the disconnect reset and leave <c>ConfigStatus == Configured</c> while
  /// <c>IsConnected == false</c> — the exact stale tier ENC-12 exists to remove — and emit a
  /// <c>Configured</c> broadcast after the disconnect one.
  /// </para>
  /// <para>
  /// It covers the compare-and-set and nothing else. <c>ConfigStatusChanged</c> is deliberately raised
  /// <b>outside</b> it: a subscriber that called back into this service while the gate was held would
  /// deadlock. The getter is an unsynchronised read of an <c>int</c>-sized enum, which is atomic; it
  /// can return a value one transition stale and no caller needs better than that.
  /// </para>
  /// </remarks>
  private readonly object _configStatusGate = new();

  /// <inheritdoc />
  public RotaryEncoderConfigStatus ConfigStatus
  {
    get => _configStatus;
    // internal rather than private only so the change-detection below and the disconnect reset in
    // RaiseConnectionChanged are testable without a device; nothing outside this assembly can write it.
    internal set
    {
      RotaryEncoderConfigStatus previous;
      bool changed;

      lock (_configStatusGate)
      {
        // The change check lives here rather than at the assignment sites, so a site added later
        // cannot introduce a duplicate broadcast by omission.
        previous = _configStatus;
        changed = previous != value;
        if (changed)
        {
          _configStatus = value;
        }
      }

      if (!changed)
      {
        return;
      }

      ConfigStatusChanged?.Invoke(this, new EncoderConfigStatusEventArgs
      {
        Status = value,
        PreviousStatus = previous,
      });
    }
  }

  /// <summary>
  /// Upper bound on the absent-device rescan interval. Presence is discovered by enumerating HID
  /// devices, and doing that every <c>ReconnectDelayMs</c> forever against a device that may simply
  /// not be installed is the "reconnect thrash" ENC-0 rules out. Backing off to this cap keeps a
  /// permanently-absent encoder cheap while still noticing a plug-in within a few seconds.
  /// </summary>
  private const int MaxAbsentRescanMs = 15000;


  public HidRotaryEncoderService(
    ILogger<HidRotaryEncoderService> logger,
    IOptionsMonitor<RotaryEncoderOptions> options,
    RotaryEncoderDesignedConfig designedConfig,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _options = options;
    _designedConfig = designedConfig;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <inheritdoc />
  public bool IsConnected => _isConnected;

  /// <inheritdoc />
  public event EventHandler<EncoderTurnedEventArgs>? EncoderTurned;

  /// <inheritdoc />
  public event EventHandler<EncoderButtonEventArgs>? ButtonPressed;

  /// <inheritdoc />
  public event EventHandler<EncoderConnectionEventArgs>? ConnectionChanged;

  /// <inheritdoc />
  public event EventHandler<EncoderConfigStatusEventArgs>? ConfigStatusChanged;

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(nameof(HidRotaryEncoderService));
    }

    _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _readTask = ReadLoopAsync(_cts.Token);
    _logger.LogInformation("Rotary encoder service started");
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (_cts != null)
    {
      await _cts.CancelAsync();
    }

    if (_readTask != null)
    {
      try
      {
        await _readTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
      }
      catch (TimeoutException)
      {
        _logger.LogWarning("Encoder read loop did not stop within timeout");
      }
      catch (OperationCanceledException) { }
    }

    _logger.LogInformation("Rotary encoder service stopped");
  }

  private async Task ReadLoopAsync(CancellationToken cancellationToken)
  {
    var opts = _options.CurrentValue;
    int absentRescanMs = Math.Max(opts.ReconnectDelayMs, 250);

    // Load the flash bookkeeping before the device is looked for. Flash outlives a restart, so
    // "never saved" must not be what the status card says merely because the encoder is unplugged
    // right now — that would be an assertion the stored hash contradicts.
    await RefreshFlashStateAsync(cancellationToken);

    while (!cancellationToken.IsCancellationRequested)
    {
      HidDevice? device = null;
      HidStream? stream = null;

      try
      {
        device = FindDevice(opts);
        if (device == null)
        {
          if (_isConnected)
          {
            _isConnected = false;
            RaiseConnectionChanged(false);
          }
          else if (!_announcedAbsence)
          {
            // Announce absence exactly once per absence, not once per rescan. A device that is
            // simply not installed must not produce a log line every couple of seconds forever.
            _announcedAbsence = true;
            _logger.LogInformation(
              "No rotary encoder found (VID=0x{VID:X4}, PID=0x{PID:X4}); will keep watching",
              opts.VendorId, opts.ProductId);
            RaiseConnectionChanged(false);
          }

          await Task.Delay(absentRescanMs, cancellationToken);
          absentRescanMs = Math.Min(absentRescanMs * 2, MaxAbsentRescanMs);
          continue;
        }

        // Found one: reset the backoff so a device that drops and returns is picked up promptly.
        absentRescanMs = opts.ReconnectDelayMs;
        _announcedAbsence = false;
        _announcedUnauthorized = false;

        stream = device.Open();
        // Infinite is correct for an event-driven device that is silent at rest: the read simply
        // waits for the next report rather than waking to discover nothing happened.
        //
        // ⚠ The consequence, recorded rather than papered over: cancellation cannot interrupt an
        // in-flight read, so StopAsync falls back to its 5 s abandon path on a quiet device. A
        // finite timeout would fix that, but only if HidSharp surfaces the expiry as
        // TimeoutException — and if it surfaces it as IOException instead, the handler below would
        // read it as a disconnect and tear the connection down once per timeout, forever. That is
        // not verifiable from here, and it is ENC-0's territory (disappears-mid-session handling),
        // so this keeps the behaviour that is known to work.
        stream.ReadTimeout = Timeout.Infinite;

        if (!_isConnected)
        {
          _isConnected = true;
          _everConnected = true;
          _logger.LogInformation("Encoder device connected: {Device}", device.GetProductName());
          RaiseConnectionChanged(true);
        }

        await ReadFromDeviceAsync(device, stream, cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex) when (IsPermissionDenied(ex))
      {
        // ENC-0a. This is a PERMANENT condition, not a transient one, and treating it as transient
        // is what made it expensive: hidraw nodes are created root-owned 0600, radio-api runs
        // unprivileged, and the device is present and enumerable — so the reader found it, failed to
        // open it, and retried every couple of seconds forever, each attempt logging a warning and a
        // stack trace. On a box where log volume correlates with audible distortion that is not a
        // cosmetic problem.
        //
        // Announced once with the remedy, then rescanned at the slowest interval. Retrying at all
        // still matters: a udev rule applied while the service is running fixes this without a
        // restart, and the operator should not have to know to restart it.
        if (!_announcedUnauthorized)
        {
          _announcedUnauthorized = true;
          _logger.LogError(ex,
            "Encoder found but not permitted to open it. This is a permissions problem, not a " +
            "hardware one — install deploy/common/99-rotaryusb-encoder.rules into " +
            "/etc/udev/rules.d/, then 'udevadm control --reload-rules && udevadm trigger " +
            "--subsystem-match=hidraw'. Rechecking every {Seconds}s until it is fixed.",
            MaxAbsentRescanMs / 1000);
        }

        if (_isConnected)
        {
          _isConnected = false;
          RaiseConnectionChanged(false);
        }

        absentRescanMs = MaxAbsentRescanMs;
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Encoder device error, will reconnect");

        if (_isConnected)
        {
          _isConnected = false;
          RaiseConnectionChanged(false);
        }
      }
      finally
      {
        stream?.Dispose();
      }

      if (!cancellationToken.IsCancellationRequested)
      {
        await Task.Delay(absentRescanMs, cancellationToken);
      }
    }

    if (_isConnected)
    {
      _isConnected = false;
      RaiseConnectionChanged(false);
    }
  }

  /// <summary>
  /// True when the device was found but could not be opened for permissions reasons.
  ///
  /// <para>
  /// ⚠ Matched partly by type <i>name</i>, which is deliberate rather than lazy: HidSharp 2.1.0
  /// declares <c>DeviceUnauthorizedAccessException</c> as <b>internal</b>, so it cannot be caught by
  /// type from outside the assembly. The BCL check is tried first and the name check is the fallback,
  /// so this keeps working if a later HidSharp makes the type public or changes its hierarchy.
  /// </para>
  /// </summary>
  private static bool IsPermissionDenied(Exception ex) =>
    ex is UnauthorizedAccessException ||
    ex.GetType().Name == "DeviceUnauthorizedAccessException";

  internal void RaiseConnectionChanged(bool isConnected)
  {
    if (!isConnected)
    {
      // ENC-12. The app cannot know what an absent device is running, so a device that was
      // Configured and is then unplugged must not keep claiming it — the badge would report a
      // healthy configuration for hardware that is not there. The reset lives here rather than at
      // the five call sites so a sixth added later cannot reintroduce the stale tier by omission.
      // Note the same value drives the host's volume clamp, and VolumeClampFor(Unknown) is the
      // TIGHT one, which is the correct direction for a device nobody can verify.
      ConfigStatus = RotaryEncoderConfigStatus.Unknown;
    }

    ConnectionChanged?.Invoke(this, new EncoderConnectionEventArgs
    {
      IsConnected = isConnected,
      WasEverConnected = _everConnected,
    });
  }

  private async Task ReadFromDeviceAsync(HidDevice device, HidStream stream, CancellationToken cancellationToken)
  {
    // Size the buffer from the device rather than a constant: report 0x04 (diagnostics) is 56 bytes
    // and larger than the 37-byte positions report, so a 37-byte buffer would truncate it and, on
    // some backends, error rather than skip.
    // Two different lengths, and conflating them defeats the feature detection. The DEVICE's
    // reported length is what says whether this firmware sends movement accumulators; the BUFFER is
    // floored to the positions-report size so a short-report device cannot cause an undersized
    // buffer. Passing the floored value to BeginConnection would make `>= 37` true by construction
    // and report movement support for firmware that has none.
    int deviceReportLength = device.GetMaxInputReportLength();
    var buffer = new byte[Math.Max(deviceReportLength, RotaryEncoderDecoder.PositionPayloadSize + 1)];

    // Re-baseline lives here, not at the call site, so a reconnect inside the read loop cannot skip
    // it. See RotaryEncoderDecoder.BeginConnection for why that matters.
    _decoder.BeginConnection(deviceReportLength);

    _logger.LogInformation(
      "Encoder report length {Length} bytes (movement accumulators: {HasMovement})",
      deviceReportLength, _decoder.HasMovement);

    // Published before the push so maintenance commands arriving on request threads can reach the
    // device from the moment it is open (ENC-8 §2.2). The read loop stays the only reader.
    _liveStream = stream;

    // ⚠ The configuration push runs CONCURRENTLY with the read loop below, and it has to.
    //
    // Since ENC-8 the read loop is the only reader, and the push's read-back is completed by
    // ParseReport from inside that loop. Awaiting the push here — which is what ENC-11 did, when the
    // push still owned an inline read of its own — means nothing can ever complete it: the loop
    // cannot start until the push returns, and the push cannot return until the loop answers it. It
    // does not hang, which is what makes it easy to miss; it times out on every attempt and settles
    // in a tier that tightens the host's volume clamp from 6 units per event to 2. Every boot would
    // leave the volume knob sluggish inside sealed furniture with nobody to press Re-apply.
    //
    // Measured on the appliance 2026-09-02: awaited, the boot push reported the timed-out tier with
    // every field "not read back"; started concurrently, it reports Configured with real read-back
    // values. The three writes still land before the loop's first read, and the device's reply waits
    // in the HID queue until the loop picks it up.
    //
    // ⚠ That measurement predates ENC-16, which moved the never-answered outcome from Degraded to
    // HardFault — nothing about the device confirms its safety fields, so it cannot sit in the tier
    // that runs the normal clamp. If this regresses now, it is a RED badge and a "volume is limited"
    // toast on every boot rather than an amber one, which is louder but no less wrong.
    Task bootPush = RunBootConfigurationPushAsync(stream, cancellationToken);

    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        int bytesRead;
        try
        {
          bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
        }
        catch (IOException)
        {
          // Device disconnected.
          return;
        }

        ParseReport(buffer, bytesRead);
      }
    }
    finally
    {
      _liveStream = null;

      // A disconnect mid-request must fail the waiter rather than leave the caller on the 2 s
      // timeout: the honest answer is "the device went away", not "the device did not confirm".
      FailPendingConfigRead(
        new IOException("Encoder disconnected while a configuration read was outstanding."));

      // The read loop does not wait for the boot push, but somebody has to observe it: an unawaited
      // faulted Task would swallow the reason the knobs are unconfigured.
      try
      {
        await bootPush;
      }
      catch (OperationCanceledException)
      {
        // Shutdown or disconnect during the push. Not a fault.
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Encoder boot configuration push did not complete");
      }
    }
  }

  /// <summary>
  /// Pushes the configuration once for a freshly opened connection, then refreshes the flash
  /// comparison.
  ///
  /// <para>
  /// ENC-11: pushed before the knobs are trusted, because until it succeeds the device may be on
  /// factory defaults — measured on this hardware as tiers (150ms x5), (80ms x15), (40ms x50), which
  /// at the host's 2% per unit is one detent from silence to full.
  /// </para>
  ///
  /// <para>
  /// Held under the maintenance lock because <c>_liveStream</c> is published before this runs: a
  /// Re-apply arriving in the window would otherwise put two pushes on one stream and two waiters on
  /// one read-back slot.
  /// </para>
  /// </summary>
  private async Task RunBootConfigurationPushAsync(HidStream stream, CancellationToken cancellationToken)
  {
    await _maintenanceLock.WaitAsync(cancellationToken);
    try
    {
      await ApplyConfigurationAsync(stream, cancellationToken);
    }
    finally
    {
      _maintenanceLock.Release();
    }

    // The flash comparison needs the bytes this connection would push, so it is recomputed per
    // connection rather than only at start-up: a reverse override set while the device was away
    // changes it.
    await RefreshFlashStateAsync(cancellationToken);
  }

  /// <summary>
  /// Pushes the host's configuration and verifies it by read-back (ENC-11, handoff §7.5-§7.6).
  ///
  /// <para>
  /// Order matters and is prescribed: reset positions, push config, ask for a read-back, then
  /// compare. The reset is belt-and-braces — positions are unused under accumulator semantics, so it
  /// is cheap, and it means a knob that has drifted for any reason starts from a known state.
  /// </para>
  ///
  /// <para>
  /// <b>The read-back is not politeness.</b> The device silently rejects configuration it does not
  /// like, so a write that appears to succeed and did not is indistinguishable from one that worked
  /// until the volume knob behaves as though it is on factory tiers.
  /// </para>
  /// </summary>
  private async Task ApplyConfigurationAsync(HidStream stream, CancellationToken cancellationToken)
  {
    // ENC-8. The designed table with the owner's direction overrides layered on. This one local is
    // both what gets encoded onto the wire and what the read-back is compared against, so a reverse
    // override reaches the verifier as well as the device. Pointing only the wire at it would make
    // every reversed knob a permanent HardFault with the volume clamp tightened to 2 units per
    // event, because `reverse` is a safety field.
    RotaryEncoderDeviceConfig desired = await _designedConfig.ResolveAsync(cancellationToken);
    byte[] configReport = RotaryEncoderConfigCodec.Encode(desired);
    byte[] resetPositions = RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.ResetPositions);

    for (int attempt = 1; attempt <= RotaryEncoderConfigVerifier.TransientAttempts; attempt++)
    {
      IReadOnlyList<RotaryEncoderConfigMismatch>? mismatches = null;
      RotaryEncoderDeviceConfig? readBack = null;

      try
      {
        stream.Write(resetPositions, 0, resetPositions.Length);
        stream.Write(configReport, 0, configReport.Length);

        // The read-config write lives inside the helper, which arms the waiter first. One read-back
        // path for the boot push and for the owner's buttons, so the two cannot drift.
        readBack = await RequestConfigReadBackAsync(stream, cancellationToken);

        if (readBack is not null)
        {
          mismatches = RotaryEncoderConfigVerifier.Compare(desired, readBack);
        }
      }
      catch (Exception ex) when (ex is IOException or ObjectDisposedException)
      {
        // The device went away mid-push — a disposed stream is the same event as a broken one here,
        // because both mean the read loop has already torn the connection down. The read loop's
        // reconnect path owns it; leaving the status un-Configured is what keeps the host's volume
        // clamp tight in the meantime.
        _logger.LogDebug(ex, "Encoder configuration push failed on attempt {Attempt}", attempt);
        ConfigStatus = RotaryEncoderConfigStatus.Transient;
        RecordAttempt(desired, readBack);
        return;
      }

      ConfigStatus = RotaryEncoderConfigVerifier.Classify(mismatches, attempt);
      RecordAttempt(desired, readBack);

      if (ConfigStatus == RotaryEncoderConfigStatus.Configured)
      {
        _logger.LogInformation("Encoder configuration applied and verified on attempt {Attempt}", attempt);
        return;
      }

      // Log only once the retry budget is spent. Attempts 1-3 are silent by design: a USB
      // peripheral missing a report on the first try is ordinary, and reporting it teaches the
      // owner to ignore the badge that matters.
      if (attempt == RotaryEncoderConfigVerifier.TransientAttempts)
      {
        LogConfigurationFailure(mismatches);
        return;
      }

      await Task.Delay(RotaryEncoderConfigVerifier.RetryBackoffMs[attempt - 1], cancellationToken);
    }
  }

  /// <summary>How long to wait for a <c>0x02</c> read-back. Unchanged from ENC-11's inline value.</summary>
  private static readonly TimeSpan ReadBackTimeout = TimeSpan.FromSeconds(2);

  /// <summary>
  /// Asks the device for its live configuration and waits for the read loop to hand it back.
  ///
  /// <para>
  /// The write is what makes the device speak, which is why this works while the read loop is parked
  /// in an infinite <c>ReadAsync</c>: the reply wakes it. There is still exactly one reader.
  /// </para>
  /// </summary>
  /// <returns>The device's configuration, or null if it did not answer within the timeout.</returns>
  private async Task<RotaryEncoderDeviceConfig?> RequestConfigReadBackAsync(
    HidStream stream, CancellationToken cancellationToken)
  {
    TaskCompletionSource<RotaryEncoderDeviceConfig> tcs = ArmConfigReadBack();

    byte[] readConfig = RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.ReadConfig);
    stream.Write(readConfig, 0, readConfig.Length);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(ReadBackTimeout);

    try
    {
      return await tcs.Task.WaitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
      // Treated as a mismatch rather than an error, exactly as ENC-11 already treats it: from the
      // host's point of view "did not confirm" and "confirmed something wrong" have the same
      // consequence, which is that the configuration is not trustworthy.
      Interlocked.CompareExchange(ref _pendingConfigRead, null, tcs);
      return null;
    }
  }

  /// <summary>
  /// Registers a waiter for the next configuration read-back, replacing any earlier one.
  /// </summary>
  /// <remarks>
  /// Internal so the read-back handoff can be tested without hardware — the part worth testing is
  /// the handoff, not the I/O.
  /// </remarks>
  internal TaskCompletionSource<RotaryEncoderDeviceConfig> ArmConfigReadBack()
  {
    var tcs = new TaskCompletionSource<RotaryEncoderDeviceConfig>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    Interlocked.Exchange(ref _pendingConfigRead, tcs);
    return tcs;
  }

  /// <summary>
  /// Claims report <c>0x02</c> — the device's configuration read-back — and hands it to whoever
  /// asked for it.
  /// </summary>
  /// <returns>
  /// True when the buffer was a configuration report, whether or not anybody was waiting for it.
  /// The caller uses that to stop the report falling through to the positions decoder.
  /// </returns>
  internal bool TryClaimConfigReadBack(byte[] data, int bytesRead)
  {
    if (!RotaryEncoderConfigCodec.TryDecode(data, bytesRead, out var readBack))
    {
      return false;
    }

    Interlocked.Exchange(ref _pendingConfigRead, null)?.TrySetResult(readBack);
    return true;
  }

  /// <summary>Fails an outstanding read-back waiter, if there is one.</summary>
  internal void FailPendingConfigRead(Exception error) =>
    Interlocked.Exchange(ref _pendingConfigRead, null)?.TrySetException(error);

  private void LogConfigurationFailure(IReadOnlyList<RotaryEncoderConfigMismatch>? mismatches)
  {
    if (mismatches is null)
    {
      // Error, not Warning: since ENC-16 an unanswered read-back after the retry budget classifies as
      // a hard fault, because it leaves the safety fields unconfirmed. It does NOT say the device
      // rejected a safety field — it said nothing at all, and that is a different sentence.
      _logger.LogError(
        "Encoder did not confirm its configuration after {Attempts} attempts, so its safety fields " +
        "are unverified. Treating acceleration as absent and clamping volume movement to {Clamp} " +
        "per event.",
        RotaryEncoderConfigVerifier.TransientAttempts,
        RotaryEncoderConfigVerifier.VolumeClampFor(ConfigStatus));
      return;
    }

    string detail = string.Join(", ", mismatches.Select(m =>
      m.EncoderIndex < 0 ? m.Field : $"enc{m.EncoderIndex}.{m.Field}"));

    if (ConfigStatus == RotaryEncoderConfigStatus.HardFault)
    {
      _logger.LogError(
        "Encoder rejected a SAFETY field: {Detail}. Volume movement is clamped to {Clamp} per event " +
        "until a push verifies.",
        detail, RotaryEncoderConfigVerifier.VolumeClampFor(ConfigStatus));
    }
    else
    {
      // Feel fields only, and read-back confirmed the safety fields, so the volume clamp is NOT
      // tightened here (ENC-16). The clamp value is logged so the line cannot drift from the code.
      _logger.LogWarning(
        "Encoder configuration not fully applied: {Detail}. The safety fields read back correctly, " +
        "so knobs stay live on the normal host clamps ({Clamp} per event on volume) and acceleration " +
        "is treated as absent.",
        detail, RotaryEncoderConfigVerifier.VolumeClampFor(ConfigStatus));
    }
  }

  /// <summary>
  /// Retains what was pushed, what the device answered, and when — the state the Settings page
  /// renders (ENC-8 Task 7).
  ///
  /// <para>
  /// <c>_lastVerifiedUtc</c> is written <b>only</b> while <see cref="ConfigStatus"/> is
  /// <see cref="RotaryEncoderConfigStatus.Configured"/>, which is the classifier's word for "the
  /// read-back matched". The status card prints it as <c>verified 07:14:02</c>, and that has to mean
  /// a comparison succeeded at 07:14:02 rather than that a push was attempted then.
  /// </para>
  /// </summary>
  /// <param name="pushed">The configuration written to the device on this attempt.</param>
  /// <param name="readBack">What the device answered, or null when it did not answer.</param>
  private void RecordAttempt(RotaryEncoderDeviceConfig pushed, RotaryEncoderDeviceConfig? readBack)
  {
    lock (_snapshotGate)
    {
      _lastPushed = pushed;
      _lastReadBack = readBack;
      _lastAttemptedUtc = _timeProvider.GetUtcNow();
      if (ConfigStatus == RotaryEncoderConfigStatus.Configured)
      {
        _lastVerifiedUtc = _lastAttemptedUtc;
      }
    }
  }

  /// <summary>
  /// Projects the retained push and read-back into the public per-field shape the Settings page
  /// renders (ENC-8 Task 2).
  ///
  /// <para>
  /// The field set comes from <see cref="RotaryEncoderConfigVerifier.Compare"/>'s own vocabulary, so
  /// the page and the verifier cannot disagree about what a field is called or which fields are
  /// safety fields.
  /// </para>
  /// </summary>
  /// <remarks>
  /// <c>internal</c> rather than <c>private</c> so the safety-field parity test can compare this
  /// projection's notion of a safety field against <see cref="RotaryEncoderConfigVerifier.Compare"/>'s
  /// own, instead of trusting that the two hand-written lists stayed in step.
  /// </remarks>
  internal static IReadOnlyList<RotaryEncoderFieldState> ProjectFields(
    RotaryEncoderDeviceConfig? pushed, RotaryEncoderDeviceConfig? readBack)
  {
    if (pushed is null)
    {
      return [];
    }

    var differing = readBack is null
      ? new HashSet<(int, string)>()
      : RotaryEncoderConfigVerifier.Compare(pushed, readBack)
          .Select(m => (m.EncoderIndex, m.Field))
          .ToHashSet();

    var rows = new List<RotaryEncoderFieldState>();

    Add(-1, "steps_per_detent", pushed.StepsPerDetent.ToString(), readBack?.StepsPerDetent.ToString(), safety: false);

    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      RotaryEncoderChannelConfig p = pushed.Encoders[i];
      RotaryEncoderChannelConfig? r = readBack?.Encoders[i];

      Add(i, "min_value", p.MinValue.ToString(), r?.MinValue.ToString(), safety: false);
      Add(i, "max_value", p.MaxValue.ToString(), r?.MaxValue.ToString(), safety: false);
      Add(i, "step_size", p.StepSize.ToString(), r?.StepSize.ToString(), safety: false);
      Add(i, "wrap", p.Wrap.ToString(), r?.Wrap.ToString(),
        safety: i == RotaryEncoderConfigDefaults.VolumeEncoderIndex);
      Add(i, "reverse", p.Reverse.ToString(), r?.Reverse.ToString(), safety: true);

      for (int t = 0; t < RotaryEncoderDeviceConfig.TiersPerEncoder; t++)
      {
        Add(i, $"tier{t + 1}_threshold_ms", p.Tiers[t].ThresholdMs.ToString(), r?.Tiers[t].ThresholdMs.ToString(), safety: false);
        Add(i, $"tier{t + 1}_multiplier", p.Tiers[t].Multiplier.ToString(), r?.Tiers[t].Multiplier.ToString(), safety: false);
      }
    }

    return rows;

    void Add(int index, string field, string designed, string? read, bool safety)
    {
      RotaryEncoderFieldAgreement agreement =
        readBack is null ? RotaryEncoderFieldAgreement.NotReadBack
        : differing.Contains((index, field)) ? RotaryEncoderFieldAgreement.Differs
        : RotaryEncoderFieldAgreement.Agrees;

      rows.Add(new RotaryEncoderFieldState(index, field, designed, read, safety, agreement));
    }
  }

  // --- IRotaryEncoderProvisioning (ENC-8 Task 8) ---------------------------------------------

  /// <inheritdoc />
  public RotaryEncoderProvisioningSnapshot GetSnapshot()
  {
    lock (_snapshotGate)
    {
      return new RotaryEncoderProvisioningSnapshot
      {
        Enabled = _options.CurrentValue.Enabled,
        IsConnected = _isConnected,
        WasEverConnected = _everConnected,
        Status = ConfigStatus,
        LastVerifiedUtc = _lastVerifiedUtc,
        LastAttemptedUtc = _lastAttemptedUtc,
        LastSavedToDeviceUtc = _lastSavedToDeviceUtc,
        Flash = _flashState,
        Fields = ProjectFields(_lastPushed, _lastReadBack),
      };
    }
  }

  /// <inheritdoc />
  public async Task<RotaryEncoderProvisioningSnapshot> ReapplyAsync(CancellationToken ct = default)
  {
    await _maintenanceLock.WaitAsync(ct);
    try
    {
      HidStream stream = _liveStream
        ?? throw new InvalidOperationException("The encoder is not connected.");

      // Deliberately the SAME method the boot path uses. A separate "maintenance push" would be a
      // second implementation of the one loop this row exists to make trustworthy.
      await ApplyConfigurationAsync(stream, ct);
      await RefreshFlashStateAsync(ct);
      return GetSnapshot();
    }
    finally
    {
      _maintenanceLock.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RotaryEncoderProvisioningSnapshot> SaveToDeviceAsync(CancellationToken ct = default)
  {
    await _maintenanceLock.WaitAsync(ct);
    try
    {
      HidStream stream = _liveStream
        ?? throw new InvalidOperationException("The encoder is not connected.");

      await ApplyConfigurationAsync(stream, ct);

      if (ConfigStatus != RotaryEncoderConfigStatus.Configured)
      {
        // Flash is left untouched on purpose. Writing an unverified configuration to flash would
        // persist exactly the state the read-back said we cannot trust, and it would do it to the
        // copy that runs during the next boot window before the app pushes.
        _logger.LogWarning(
          "Not writing encoder flash: the configuration did not verify (status {Status})", ConfigStatus);
        return GetSnapshot();
      }

      byte[] saveCommand = RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.SaveConfig);
      stream.Write(saveCommand, 0, saveCommand.Length);

      // The bytes recorded are the bytes just verified, taken from the same retained object the
      // Settings page renders. This is what makes the button's copy true (ENC-8 plan §0.5).
      RotaryEncoderDeviceConfig flashed;
      lock (_snapshotGate)
      {
        flashed = _lastPushed!;
      }

      await RecordFlashWriteAsync(flashed, ct);
      // Says what happened and no more: the command went out. The protocol has no acknowledgement
      // for SaveConfig (0x01), and ReadConfig (0x04) reads live RAM rather than flash, so nothing
      // available here can confirm the device stored it.
      _logger.LogInformation(
        "Sent save-to-flash command (0x01) to the encoder; the device does not acknowledge it, so the write is not independently confirmed");
      return GetSnapshot();
    }
    finally
    {
      _maintenanceLock.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> ResetCountersAsync(CancellationToken ct = default)
  {
    await _maintenanceLock.WaitAsync(ct);
    try
    {
      HidStream? stream = _liveStream;
      if (stream is null)
      {
        return false;
      }

      byte[] cmd = RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.ResetDiagnostics);
      stream.Write(cmd, 0, cmd.Length);

      // ⚠ Returns "the command was sent", NOT "the counters are zero". The protocol offers no
      // acknowledgement for 0x03/0x05 and this build has no diagnostics decoder (report 0x04 is
      // ENC-14), so there is nothing to verify against. The UI copy says exactly this much and no
      // more.
      _logger.LogInformation("Sent encoder counter-reset command");
      return true;
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
    {
      _logger.LogWarning(ex, "Encoder counter-reset could not be sent");
      return false;
    }
    finally
    {
      _maintenanceLock.Release();
    }
  }

  /// <inheritdoc />
  public async Task<RotaryEncoderProvisioningSnapshot> SetReverseAsync(
    int encoderIndex, bool reverse, CancellationToken ct = default)
  {
    await _designedConfig.SetReverseAsync(encoderIndex, reverse, ct);
    // Push immediately, per handoff §7.8 card 3: "toggling one pushes immediately (0x02 + verify)".
    // A stored-but-unpushed direction is the same lie as an unverified push.
    return await ReapplyAsync(ct);
  }

  /// <summary>
  /// Recomputes the flash-staleness state by comparing the stored hash of the last flashed bytes
  /// against a hash of the bytes the app would push right now.
  ///
  /// <para>
  /// A timestamp alone cannot support the words "differs from current design" — that is a claim
  /// about bytes, so bytes are what is compared. See ENC-8 plan §2.4.
  /// </para>
  /// </summary>
  private async Task RefreshFlashStateAsync(CancellationToken ct)
  {
    string? storedHash = await _designedConfig.GetLastSavedHashAsync(ct);
    DateTimeOffset? storedAt = await _designedConfig.GetLastSavedUtcAsync(ct);

    string currentHash = HashOf(await _designedConfig.ResolveAsync(ct));

    lock (_snapshotGate)
    {
      _lastSavedToDeviceUtc = storedAt;
      _flashState = storedHash is null
        ? RotaryEncoderFlashState.NeverSaved
        : string.Equals(storedHash, currentHash, StringComparison.Ordinal)
          ? RotaryEncoderFlashState.MatchesCurrentDesign
          : RotaryEncoderFlashState.DiffersFromCurrentDesign;
    }
  }

  private async Task RecordFlashWriteAsync(RotaryEncoderDeviceConfig flashed, CancellationToken ct)
  {
    await _designedConfig.RecordFlashWriteAsync(_timeProvider.GetUtcNow(), HashOf(flashed), ct);
    await RefreshFlashStateAsync(ct);
  }

  private static string HashOf(RotaryEncoderDeviceConfig config) =>
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
      RotaryEncoderConfigCodec.Encode(config)));

  private void ParseReport(byte[] data, int bytesRead)
  {
    // Report 0x02 is the device's configuration read-back. The decoder ignores it by design
    // (RotaryEncoderDecoder.Decode returns false for anything that is not report 0x01), so it is
    // claimed here, before that early return, and handed to whoever asked for it.
    if (TryClaimConfigReadBack(data, bytesRead))
    {
      return;
    }

    if (!_decoder.Decode(data, bytesRead))
    {
      return;
    }

    for (int i = 0; i < RotaryEncoderDecoder.EncoderCount; i++)
    {
      bool? pressed = _decoder.ButtonChanges[i];
      if (pressed.HasValue)
      {
        ButtonPressed?.Invoke(this, new EncoderButtonEventArgs
        {
          EncoderIndex = i,
          IsPressed = pressed.Value
        });
      }
    }

    for (int i = 0; i < RotaryEncoderDecoder.EncoderCount; i++)
    {
      int delta = _decoder.Deltas[i];
      if (delta != 0)
      {
        EncoderTurned?.Invoke(this, new EncoderTurnedEventArgs
        {
          EncoderIndex = i,
          Delta = delta
        });
      }
    }
  }

  /// <summary>
  /// Latest clamped position for an encoder, in device units.
  ///
  /// <para>
  /// Prefer this over accumulating <see cref="EncoderTurned"/> deltas for a <b>bounded</b> control
  /// such as volume: the device owns the range and clamping at the ends is the wanted behaviour.
  /// Deltas are the right input where the host owns the range, such as tuning.
  /// </para>
  /// </summary>
  public int GetPosition(int encoderIndex)
  {
    if (encoderIndex < 0 || encoderIndex >= RotaryEncoderDecoder.EncoderCount)
    {
      throw new ArgumentOutOfRangeException(nameof(encoderIndex));
    }

    return _decoder.GetPosition(encoderIndex);
  }

  private HidDevice? FindDevice(RotaryEncoderOptions opts)
  {
    try
    {
      // If a specific device path is configured, use it directly
      if (!string.IsNullOrEmpty(opts.DevicePath))
      {
        return DeviceList.Local.GetHidDevices()
          .FirstOrDefault(d => d.DevicePath == opts.DevicePath);
      }

      // Otherwise find by VID/PID
      return DeviceList.Local.GetHidDevices(opts.VendorId, opts.ProductId)
        .FirstOrDefault();
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error enumerating HID devices");
      return null;
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    _cts?.Cancel();
    _cts?.Dispose();
    _maintenanceLock.Dispose();
    GC.SuppressFinalize(this);
  }
}
