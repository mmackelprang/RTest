using System.Globalization;
using RTLSDRCore.Bands;
using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using RTLSDRCore.Models;

namespace RTLSDRCore;

/// <summary>Why a WFM chain was built, for the FM acquisition watchdog (AUD-106).</summary>
/// <param name="Reason">For the log: "band change", "frequency tune", "start-up", "modulation change", or the watchdog step.</param>
/// <param name="ByRecovery">Whether a watchdog recovery step built it.</param>
/// <param name="Generation">
/// The tune generation the chain was published at (the receiver advances it when it publishes a WFM
/// chain). Blocks stamped earlier were raised before the chain existed and are not judged for it.
/// </param>
internal sealed record FmChainStart(string Reason, bool ByRecovery, int Generation = 0);

/// <summary>
/// AUD-106: the FM stereo/RDS-loss watchdog's receiver side. <see cref="FmAcquisitionWatchdog"/> decides;
/// this part feeds it from the DSP thread, logs each FM start's outcome and, on a miss, a diagnostic
/// snapshot, and runs the recovery steps off the DSP thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why off the DSP thread.</b> A recovery step stops and restarts streaming and takes
/// <c>_lock</c>, like any band change from the API. Run on the DSP thread it would hold up audio for its
/// whole duration and wait on <c>_lock</c> behind whatever control call holds it. The steps and the
/// snapshot's device reads are therefore chained on the thread pool (<see cref="LastWatchdogTask"/>),
/// one at a time, in the order the DSP thread raised them.
/// </para>
/// <para>
/// <b>What the snapshot can and cannot see.</b> The device fields are librtlsdr's <i>recorded</i>
/// state (<c>rtlsdr_get_direct_sampling</c>, <c>_get_center_freq</c>, <c>_get_sample_rate</c> and
/// <c>_get_tuner_gain</c> return fields of its device struct, not register reads), so they cannot show
/// a tuner or RTL2832 register left wrong by the HF-off path (the dossier's hypothesis 1); a centre of
/// 0 does show a tune that failed. The evidence for hypothesis 1 is in the signal: the composite's mean
/// (the discriminator reads 75 kHz deviation as 1.0, so the mean times 75 kHz estimates how far the
/// carrier is off centre), the IQ DC offset and I/Q power ratio of one block, and librtlsdr's own
/// <c>[R82XX] PLL not locked!</c> on stderr, which reaches journald (<c>journalctl -u radio-api</c>).
/// Hypothesis 2 (DSP decoder state): the pilot level, the PLL frequency and the RDS sync state.
/// </para>
/// </remarks>
public partial class RadioReceiver
{
  /// <summary>WFM peak deviation that the discriminator reads as 1.0, in Hz.</summary>
  private const double WfmDeviationHz = 75_000;

  /// <summary>
  /// The pilot level (<see cref="StereoFmDecoder.PilotStrength"/>) above which a pilot can count as
  /// locked: three times the stereo detector's 0.01 threshold. A broadcast pilot at 10 % deviation reads
  /// about 0.06.
  /// </summary>
  internal const float LockedPilotLevel = 0.03f;

  /// <summary>How close the PLL must sit to 19 kHz for a pilot to count as locked, in Hz.</summary>
  internal const float LockedPllToleranceHz = 5f;

  /// <summary>The FM acquisition watchdog (AUD-106). Its settings are open to tests.</summary>
  internal FmAcquisitionWatchdog FmWatchdog { get; } = new();

  /// <summary>
  /// The last chained watchdog task (logging the outcome, the device snapshot, and any recovery step).
  /// Tests wait on it; null until the first outcome.
  /// </summary>
  internal Task? LastWatchdogTask { get; private set; }

  /// <summary>Raised on the DSP thread for every outcome the watchdog returns (tests count them).</summary>
  internal event Action<FmAcquisitionOutcome>? FmAcquisitionReported;

  /// <summary>
  /// Whether <paramref name="stereo"/>'s pilot counts as locked: strong (<see cref="LockedPilotLevel"/>)
  /// and with the PLL within <see cref="LockedPllToleranceHz"/> of 19 kHz. Stronger evidence than
  /// <see cref="StereoFmDecoder.StereoDetected"/>, which is an energy detector and reads noise as a
  /// pilot (`AUD-109`). Only this, or RDS sync, records a frequency as locked for the watchdog.
  /// </summary>
  internal static bool IsPilotLocked(StereoFmDecoder stereo) =>
    stereo.PilotStrength >= LockedPilotLevel && Math.Abs(stereo.PllFrequency - 19_000f) <= LockedPllToleranceHz;

  /// <summary>
  /// Publishes a new WFM chain to the watchdog: called where the chain is built, with the reason the
  /// caller left in <c>_pendingFmStart</c>. Advances the tune generation, so a block raised before the
  /// chain existed (still queued) is never judged as this start's first block.
  /// </summary>
  private void PublishFmStart()
  {
    FmChainStart pending = _pendingFmStart ?? new FmChainStart("band change", ByRecovery: false);
    _pendingFmStart = null;
    int generation = Interlocked.Increment(ref _tuneGeneration);
    _fmStart = pending with { Generation = generation };
  }

  /// <summary>
  /// Feeds one demodulated WFM block to the watchdog. Called on the DSP thread from
  /// <c>ProcessSamples</c>, after the stereo and RDS decoders have run on the block's composite.
  /// </summary>
  private void ObserveFmAcquisition(IqSample[] samples, int tuneGeneration, ReadOnlySpan<float> composite)
  {
    StereoFmDecoder? stereo = _stereoDecoder;
    RdsDecoder? rds = _rdsDecoder;
    if (stereo is null)
    {
      return;
    }

    FmChainStart? start = _fmStart;
    if (start is not null && !ReferenceEquals(start, _watchedFmStart))
    {
      if (tuneGeneration < start.Generation)
      {
        // Raised before this chain was published: neither this start's first block nor a retune.
        return;
      }

      _watchedFmStart = start;
      FmWatchdog.BeginStart(_currentFrequencyHz, tuneGeneration, start.Reason, start.ByRecovery);
    }

    FmAcquisitionOutcome? outcome = FmWatchdog.Observe(
      tuneGeneration, _currentFrequencyHz, samples.Length, stereo.StereoDetected, rds?.IsSynchronized ?? false,
      _lastSignalStrength, IsPilotLocked(stereo));
    if (outcome is null)
    {
      return;
    }

    FmAcquisitionReported?.Invoke(outcome);

    string? signalSnapshot = outcome.Missed ? SignalSnapshot(samples, composite, stereo, rds) : null;
    float pilotLevel = stereo.PilotStrength;
    float pllHz = stereo.PllFrequency;
    int sdrRate = _sdrSampleRate;
    Task previous = LastWatchdogTask ?? Task.CompletedTask;
    LastWatchdogTask = previous.ContinueWith(
      _ => HandleFmAcquisitionOutcome(outcome, tuneGeneration, signalSnapshot, pilotLevel, pllHz, sdrRate),
      CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
  }

  /// <summary>The signal half of the snapshot, from this block (DSP thread).</summary>
  private static string SignalSnapshot(IqSample[] samples, ReadOnlySpan<float> composite, StereoFmDecoder stereo, RdsDecoder? rds)
  {
    double sumI = 0, sumQ = 0, powI = 0, powQ = 0;
    foreach (IqSample s in samples)
    {
      sumI += s.I;
      sumQ += s.Q;
      powI += (double)s.I * s.I;
      powQ += (double)s.Q * s.Q;
    }
    int n = Math.Max(samples.Length, 1);
    double meanI = sumI / n, meanQ = sumQ / n;
    double varI = powI / n - meanI * meanI, varQ = powQ / n - meanQ * meanQ;
    double iqRatioDb = varQ > 0 && varI > 0 ? 10.0 * Math.Log10(varI / varQ) : double.NaN;

    double compSum = 0, compPow = 0;
    foreach (float v in composite)
    {
      compSum += v;
      compPow += (double)v * v;
    }
    int m = Math.Max(composite.Length, 1);
    double compMean = compSum / m;
    double compRms = Math.Sqrt(compPow / m);

    return string.Create(CultureInfo.InvariantCulture,
      $"IQ DC I={meanI:F4} Q={meanQ:F4}, I/Q power ratio {iqRatioDb:F2} dB, " +
      $"composite mean {compMean:F4} (carrier offset ≈ {compMean * WfmDeviationHz:F0} Hz; wraps beyond ±120 kHz), composite RMS {compRms:F3}, " +
      $"PLL {stereo.PllFrequency:F1} Hz, " +
      $"RDS {(rds is null ? "none" : rds.IsSynchronized ? "synced" : "not synced")}");
  }

  /// <summary>
  /// The device half of the snapshot: librtlsdr's recorded state, not register reads (see the class
  /// remarks). Thread pool, under <c>_tuneLock</c>.
  /// </summary>
  private string DeviceSnapshot()
  {
    lock (_tuneLock)
    {
      int? recorded = _device.IsOpen ? _device.ReadDriverDirectSamplingMode() : null;
      return string.Create(CultureInfo.InvariantCulture,
        $"device {_device.DeviceInfo.Product} tuner {_device.DeviceInfo.TunerType}, open {_device.IsOpen}, streaming {_device.IsStreaming}, " +
        $"librtlsdr records direct sampling {(recorded?.ToString(CultureInfo.InvariantCulture) ?? "unknown")} (this wrapper: {(_device.IsHfMode ? "HF" : "off")}), " +
        $"centre {(_device.IsOpen ? _device.GetFrequency() : 0)} Hz, rate {(_device.IsOpen ? _device.GetSampleRate() : 0)} S/s, " +
        $"tuner gain {(_device.IsOpen ? _device.GetGain() : 0f):F1} dB (auto {_autoGain}, manual {_manualGain:F1} dB)");
    }
  }

  private void HandleFmAcquisitionOutcome(FmAcquisitionOutcome outcome, int generation, string? signalSnapshot,
    float pilotLevel, float pllHz, int sdrRate)
  {
    try
    {
      string pilot = Millis(outcome.PilotAfterSamples, sdrRate);
      string rds = Millis(outcome.RdsAfterSamples, sdrRate);
      string window = Millis(outcome.WindowSamples, sdrRate);
      string frequency = RadioBand.FormatFrequency(outcome.FrequencyHz);
      string? snapshot = signalSnapshot is null ? null : $"{signalSnapshot}; {DeviceSnapshot()}";

      switch (outcome.Action)
      {
        case FmWatchdogAction.ReportOutcome:
          Logger.Information(
            "FM acquisition ({Reason}) on {Frequency}: pilot {Pilot}, RDS sync {Rds} (judged over {Window}; pilot level {PilotLevel:F4}, PLL {Pll:F1} Hz, mean signal {Signal:F2})",
            outcome.Reason, frequency, pilot, rds, window, pilotLevel, pllHz, outcome.MeanSignal);
          if (snapshot is not null)
          {
            Logger.Information("FM acquisition snapshot on {Frequency} (no pilot, no RDS): {Snapshot}", frequency, snapshot);
          }
          break;

        case FmWatchdogAction.RerunBandSetup:
        case FmWatchdogAction.ReopenDevice:
          Logger.Warning(
            "FM watchdog: no pilot and no RDS on {Frequency} ({Reason}) within {Window} at mean signal {Signal:F2}, " +
            "a frequency locked earlier; step {Step}: {Action}. Snapshot: {Snapshot}",
            frequency, outcome.Reason, window, outcome.MeanSignal, outcome.RecoveryStep,
            outcome.Action == FmWatchdogAction.RerunBandSetup
              ? "re-running the band setup (HF on and off, sample rate, tune, gain)"
              : "closing and reopening the device",
            snapshot);
          RecoverFm(outcome.Action == FmWatchdogAction.ReopenDevice, outcome.RecoveryStep, outcome.FrequencyHz, generation);
          break;

        case FmWatchdogAction.GiveUp:
          Logger.Warning(
            "FM watchdog: no pilot and no RDS on {Frequency} ({Reason}) within {Window} at mean signal {Signal:F2}; " +
            "giving up until an FM acquisition is healthy or FM is selected again (recovery steps tried, or the hourly limit of {Limit} reached). Snapshot: {Snapshot}",
            frequency, outcome.Reason, window, outcome.MeanSignal, FmWatchdog.MaxEpisodesPerHour, snapshot);
          break;
      }
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "FM watchdog: error handling an acquisition outcome");
    }
  }

  private static string Millis(long? samples, int sdrRate) =>
    samples is null || sdrRate <= 0
      ? "none"
      : string.Create(CultureInfo.InvariantCulture, $"{samples.Value * 1000 / sdrRate} ms");

  /// <summary>
  /// A recovery step, with streaming paused. Step 1 switches HF direct sampling on and off again (on a
  /// device that has it: librtlsdr's HF-off path re-initialises the tuner, the path the dossier's
  /// hypothesis 1 suspects), then rebuilds the signal chain and sets the sample rate, the frequency and
  /// the gain. Step 2 closes and reopens the device first, then does the same.
  /// </summary>
  /// <remarks>
  /// Skipped, with an Information line, when anything moved since the miss was judged: the receiver is
  /// not running, a sweep or seek is under way, it is no longer on FM, or it has been retuned or
  /// rebuilt since (<paramref name="generation"/>), for example by a person pressing a preset while the
  /// step waited for <c>_lock</c>. A skipped step still counts as taken in the episode.
  /// </remarks>
  private void RecoverFm(bool reopen, int step, long frequencyHz, int generation)
  {
    lock (_lock)
    {
      if (_state != ReceiverState.Running || _currentBand.Type != BandType.FM || IsSweeping
          || _currentFrequencyHz != frequencyHz || Volatile.Read(ref _tuneGeneration) != generation)
      {
        Logger.Information(
          "FM watchdog: step {Step} skipped; the receiver moved on since the miss ({State} on {Band} at {Frequency})",
          step, _state, _currentBand.Name, RadioBand.FormatFrequency(_currentFrequencyHz));
        return;
      }

      bool wasStreaming = _device.IsStreaming;
      lock (_tuneLock)
      {
        if (wasStreaming)
        {
          _device.StopStreaming();
        }

        if (reopen)
        {
          _device.Close();
        }
      }

      if (reopen && !_device.Open())
      {
        // The same teardown as a failed start (processing thread, queue, subscription), so a later
        // Startup from Error starts clean instead of subscribing a second time.
        Logger.Error("FM watchdog: step {Step}: the device did not reopen; the receiver is stopped in Error", step);
        CloseDeviceAfterFailedStartup();
        SetState(ReceiverState.Error);
        return;
      }

      try
      {
        if (_device.SupportsHfDirectSampling)
        {
          lock (_tuneLock)
          {
            if (!_device.SetHfMode(true) | !_device.SetHfMode(false))
            {
              Logger.Warning("FM watchdog: step {Step}: the HF on/off cycle was refused (HF direct sampling now {Mode})",
                step, _device.IsHfMode ? "on" : "off");
            }
          }
        }

        _pendingFmStart = new FmChainStart(
          reopen ? "watchdog step 2: device reopened" : "watchdog step 1: band setup re-run", ByRecovery: true);
        SetupSignalProcessing();
        if (!_device.SetSampleRate(_sdrSampleRate))
        {
          Logger.Warning("FM watchdog: step {Step}: the device refused the sample rate {SampleRate} Hz", step, _sdrSampleRate);
        }

        bool tuned;
        lock (_tuneLock)
        {
          tuned = _device.SetFrequency(DeviceFrequency(_currentFrequencyHz));
        }
        Interlocked.Increment(ref _tuneGeneration);
        if (!tuned)
        {
          Logger.Warning("FM watchdog: step {Step}: the device refused {Frequency}", step, RadioBand.FormatFrequency(_currentFrequencyHz));
        }

        _device.SetGainMode(_autoGain);
        if (!_autoGain)
        {
          _device.SetGain(_manualGain);
        }
      }
      finally
      {
        // Whatever failed above, streaming is restarted if it was running.
        if (ResumeAfterBandChange(wasStreaming))
        {
          Logger.Information("FM watchdog: step {Step} done on {Frequency}", step, RadioBand.FormatFrequency(_currentFrequencyHz));
        }
      }
    }
  }
}
