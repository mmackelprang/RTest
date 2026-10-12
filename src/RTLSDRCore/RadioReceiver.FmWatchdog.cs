using System.Globalization;
using RTLSDRCore.Bands;
using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using RTLSDRCore.Models;

namespace RTLSDRCore;

/// <summary>Why a WFM chain was built, for the FM acquisition watchdog (AUD-106).</summary>
/// <param name="Reason">For the log: "band change", "start-up", "modulation change", or the watchdog step.</param>
/// <param name="ByRecovery">Whether a watchdog recovery step built it.</param>
internal sealed record FmChainStart(string Reason, bool ByRecovery);

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
/// <b>The snapshot</b> separates the two leading hypotheses in the AUD-106 dossier. Hypothesis 1 (tuner
/// or RTL2832 register state after HF-off): the device's live direct-sampling mode
/// (<c>rtlsdr_get_direct_sampling</c>) against the recorded one, the centre frequency, sample rate and
/// gain read from the driver, the IQ DC offset and I/Q power ratio of one block, and the composite's
/// mean, which a carrier off-centre shows as a DC offset (75 kHz deviation reads 1.0, so the mean times
/// 75 kHz estimates the offset). Hypothesis 2 (DSP decoder state): the pilot level, the PLL frequency
/// and the RDS sync state.
/// </para>
/// </remarks>
public partial class RadioReceiver
{
  /// <summary>WFM peak deviation that the discriminator reads as 1.0, in Hz.</summary>
  private const double WfmDeviationHz = 75_000;

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
      _watchedFmStart = start;
      FmWatchdog.BeginStart(_currentFrequencyHz, tuneGeneration, start.Reason, start.ByRecovery);
    }

    FmAcquisitionOutcome? outcome = FmWatchdog.Observe(
      tuneGeneration, _currentFrequencyHz, samples.Length, stereo.StereoDetected, rds?.IsSynchronized ?? false,
      _lastSignalStrength);
    if (outcome is null)
    {
      return;
    }

    FmAcquisitionReported?.Invoke(outcome);

    string? signalSnapshot = outcome.Missed ? SignalSnapshot(samples, composite, stereo, rds) : null;
    int sdrRate = _sdrSampleRate;
    Task previous = LastWatchdogTask ?? Task.CompletedTask;
    LastWatchdogTask = previous.ContinueWith(
      _ => HandleFmAcquisitionOutcome(outcome, signalSnapshot, sdrRate),
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
      $"composite mean {compMean:F4} (carrier offset ≈ {compMean * WfmDeviationHz:F0} Hz), composite RMS {compRms:F3}, " +
      $"pilot level {stereo.PilotStrength:F4} (stereo at > 0.01), PLL {stereo.PllFrequency:F1} Hz, " +
      $"RDS {(rds is null ? "none" : rds.IsSynchronized ? "synced" : "not synced")}");
  }

  /// <summary>The device half of the snapshot (thread pool, under <c>_tuneLock</c>).</summary>
  private string DeviceSnapshot()
  {
    lock (_tuneLock)
    {
      int? live = _device.IsOpen ? _device.ReadLiveDirectSamplingMode() : null;
      return string.Create(CultureInfo.InvariantCulture,
        $"device {_device.DeviceInfo.Product} tuner {_device.DeviceInfo.TunerType}, open {_device.IsOpen}, streaming {_device.IsStreaming}, " +
        $"direct sampling live {(live?.ToString(CultureInfo.InvariantCulture) ?? "unknown")} / recorded {(_device.IsHfMode ? "HF" : "off")}, " +
        $"centre {(_device.IsOpen ? _device.GetFrequency() : 0)} Hz, rate {(_device.IsOpen ? _device.GetSampleRate() : 0)} S/s, " +
        $"gain {(_device.IsOpen ? _device.GetGain() : 0f):F1} dB (auto {_autoGain}, manual {_manualGain:F1} dB)");
    }
  }

  private void HandleFmAcquisitionOutcome(FmAcquisitionOutcome outcome, string? signalSnapshot, int sdrRate)
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
            "FM acquisition ({Reason}) on {Frequency}: pilot {Pilot}, RDS sync {Rds} (window {Window}, mean signal {Signal:F2})",
            outcome.Reason, frequency, pilot, rds, window, outcome.MeanSignal);
          if (snapshot is not null)
          {
            Logger.Information("FM acquisition snapshot on {Frequency} (no pilot, no RDS): {Snapshot}", frequency, snapshot);
          }
          break;

        case FmWatchdogAction.RerunBandSetup:
        case FmWatchdogAction.ReopenDevice:
          Logger.Warning(
            "FM watchdog: no pilot and no RDS on {Frequency} ({Reason}) within {Window} at mean signal {Signal:F2}, " +
            "a frequency that had them earlier; step {Step}: {Action}. Snapshot: {Snapshot}",
            frequency, outcome.Reason, window, outcome.MeanSignal, outcome.RecoveryStep,
            outcome.Action == FmWatchdogAction.RerunBandSetup ? "re-running the band setup" : "closing and reopening the device",
            snapshot);
          RecoverFm(outcome.Action == FmWatchdogAction.ReopenDevice, outcome.RecoveryStep);
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
      : string.Create(CultureInfo.InvariantCulture, $"after {samples.Value * 1000 / sdrRate} ms");

  /// <summary>
  /// A recovery step: re-run the FM band setup (HF mode, signal chain, sample rate, tune, gain) with
  /// streaming paused, and for step 2 close and reopen the device first. Skipped when the receiver is
  /// no longer running on FM (the user moved on while the step was queued).
  /// </summary>
  private void RecoverFm(bool reopen, int step)
  {
    lock (_lock)
    {
      if (_state != ReceiverState.Running || _currentBand.Type != BandType.FM)
      {
        Logger.Information("FM watchdog: step {Step} skipped; the receiver is {State} on {Band}", step, _state, _currentBand.Name);
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
        Logger.Error("FM watchdog: step {Step}: the device did not reopen", step);
        SetState(ReceiverState.Error);
        return;
      }

      if (ApplyHfModeFor(_currentBand, _currentFrequencyHz) != TuneResult.Success)
      {
        Logger.Warning("FM watchdog: step {Step}: the device refused HF direct sampling off", step);
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

      if (!ResumeAfterBandChange(wasStreaming))
      {
        return;
      }

      Logger.Information("FM watchdog: step {Step} done on {Frequency}", step, RadioBand.FormatFrequency(_currentFrequencyHz));
    }
  }
}
