using System.Collections.Concurrent;
using RTLSDRCore.Bands;
using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using RTLSDRCore.Sweep;
using Serilog;

namespace RTLSDRCore;

  /// <summary>
  /// Main radio receiver class that coordinates hardware, signal processing, and audio output
  /// </summary>
  public partial class RadioReceiver : IRadioControl, IDisposable
  {
      private readonly ILogger Logger;

      private readonly ISdrDevice _device;
      private readonly object _lock = new();

      private RadioBand _currentBand;
      private long _currentFrequencyHz;
      private ModulationType _currentModulation;
      private ReceiverState _state = ReceiverState.Stopped;

      private IDemodulator _demodulator;
      // AUD-101: the AM-modulation chain (AM, Shortwave, Aircraft), or null on every other
      // modulation. Replaced as a whole by SetupSignalProcessing; the DSP thread reads it once
      // per block.
      private volatile AmReceiveChain? _amChain;
      // AUD-107: the narrowband FM chain (Weather, VHF), or null on every other modulation.
      // Replaced as a whole by SetupSignalProcessing; the DSP thread reads it once per block.
      // Never non-null at the same time as _amChain once a setup has finished.
      private volatile NfmReceiveChain? _nfmChain;

      // AUD-106: the FM acquisition watchdog. _fmStart is replaced (as a whole) each time a WFM chain
      // is built, carrying why and the tune generation it was published at; the DSP thread compares it
      // with the last one it saw to open a start window. _pendingFmStart is the reason the next WFM
      // chain records, set by the caller just before the call that builds it (null: a band change).
      private volatile FmChainStart? _fmStart;
      private FmChainStart? _watchedFmStart;
      private FmChainStart? _pendingFmStart;
      // Counts the receiver's tunes of the device, and (AUD-106) each WFM chain it publishes. Each IQ
      // block is stamped with the value current when the USB thread raised it (OnSamplesAvailable),
      // and the AM chain re-seeds its DC blocker and AGC on the second block stamped with a new value
      // (AmReceiveChain.BeginBlock).
      private int _tuneGeneration;

      // One raised IQ block and the tune generation current when it was raised. Stamping at raise
      // time, not at dequeue time, keeps "the second block after a tune holds only the new
      // frequency" true however many blocks are waiting in the processing queue.
      private readonly record struct IqBlock(IqSample[] Samples, int TuneGeneration);
      private Decimator? _iqDecimator;
      private DeEmphasisFilter? _deEmphasis;
      private DeEmphasisFilter? _deEmphasisRight; // second channel for stereo
      private AudioDecimator? _audioDecimator;
      private AudioDecimator? _audioDecimatorRight; // second channel for stereo
      private StereoFmDecoder? _stereoDecoder;
      private RdsDecoder? _rdsDecoder;
      private int _sdrSampleRate;
      private int _demodSampleRate;

      // Pre-allocated processing buffers to avoid GC pressure.
      // Sized in SetupSignalProcessing based on the expected USB chunk size.
      private float[] _demodBuffer = Array.Empty<float>();
      private float[] _stereoDemodBuffer = Array.Empty<float>(); // interleaved L,R at demod rate
      private float[] _decimBuffer = Array.Empty<float>();
      private float[] _decimBufferRight = Array.Empty<float>(); // right channel demod (de-interleave + de-emphasis)
      private float[] _decimBufferRightOut = Array.Empty<float>(); // right channel decimation output
      private IqSample[] _iqDecimBuffer = Array.Empty<IqSample>();
      // Pre-allocated silence buffer delivered when squelch closes or muted.
      // Keeps the downstream BufferedSoundGenerator fed at a consistent rate
      // to prevent underruns (which appear as zero-filled gaps in the waveform).
      private float[] _squelchSilenceBuffer = Array.Empty<float>();
      private int _expectedAudioOutputCount;

      private AudioFormat _audioFormat = AudioFormat.Default;
      private float _volume = 1.0f;
      private float _squelchThreshold = 0.1f;
      private bool _isMuted;
      private float _lastSignalStrength;
      private bool _autoGain = true;
      private float _manualGain = 30.0f;

      private CancellationTokenSource? _processingCts;
      private CancellationTokenSource? _scanCts;
      private bool _isScanning;

      // Async processing queue: USB read thread enqueues IQ samples,
      // a dedicated processing thread dequeues and runs DSP. This decouples
      // USB timing from DSP processing overhead, preventing sample rate
      // deficit caused by synchronous processing blocking the read loop.
      private BlockingCollection<IqBlock>? _processingQueue;
      private Thread? _processingThread;

      // Live band sweep (AUD-76). See SweepChannels for the rules.
      // _tuneLock serialises every native SetFrequency / gain call made by
      // SetFrequencyInternal, the sweep and Shutdown's close.
      private readonly object _tuneLock = new();
      // Guards _sweepCts and _isSweeping transitions.
      private readonly object _sweepStateLock = new();
      private CancellationTokenSource? _sweepCts;
      private volatile bool _isSweeping;
      // ORed into the squelch/mute check in ProcessSamples. Separate from the
      // user's _isMuted so a sweep never changes what IsMuted reports.
      private volatile bool _sweepMuted;
      // Blocks still to be muted after a sweep ends — covers the USB block
      // that was in flight across the final retune. Written by the sweep
      // thread, consumed by the DSP thread.
      private volatile int _sweepMuteTailBlocks;
      // Receives a copy of each IQ block while a sweep runs; null otherwise.
      private volatile Action<IqSample[]>? _sweepTap;
      // Set while no sweep is running; Shutdown waits on it before closing.
      private readonly ManualResetEventSlim _sweepIdle = new(initialState: true);

      private const int SweepMuteTailBlockCount = 2;
      private static readonly TimeSpan SweepIdleWaitTimeout = TimeSpan.FromSeconds(3);

      // Sample rate configuration
      private const int DefaultSdrSampleRate = 2_400_000;
      private const int DefaultAudioSampleRate = 48_000;

      /// <inheritdoc/>
      public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;

      /// <inheritdoc/>
      public event EventHandler<SignalStrengthEventArgs>? SignalStrengthUpdated;

      /// <inheritdoc/>
      public event EventHandler<ReceiverStateChangedEventArgs>? StateChanged;

      /// <inheritdoc/>
      public event EventHandler<FrequencyChangedEventArgs>? FrequencyChanged;

      /// <summary>
      /// Raised every time the RDS decoder produces a complete Program
      /// Service (PS) name candidate from an RDS group 0A/0B frame.
      /// Fires at the natural RDS PS frame cadence (~10 Hz when the signal
      /// is clean), independent of how often consumers read
      /// <see cref="RdsStationName"/>. The argument carries the raw candidate
      /// from a single frame — subscribers must apply their own stability
      /// filter before treating the value as a real station identifier.
      /// </summary>
      /// <remarks>
      /// This is the right hook for a downstream
      /// <c>RdsStationNameStabilityTracker</c>: it samples at the underlying
      /// frame rate rather than at the rate consumers happen to poll the
      /// property, so mid-roll rolling-PS fragments do not get promoted to
      /// "stable" just because the property happens to be read while a
      /// fragment is displayed.
      /// </remarks>
      public event EventHandler<RdsStationNameChangedEventArgs>? RdsStationNameChanged;

      /// <summary>
      /// Raised when the RDS PI (Program Identification) code is decoded for
      /// the first time after RDS lock, and on any subsequent change (rare —
      /// typically only on frequency change). Task #80 v4 — consumed by
      /// <c>SDRRadioAudioSource</c> to decode the call sign via NRSC-4-B
      /// Annex D and surface it as the stable station identifier.
      /// </summary>
      /// <remarks>
      /// PI is a 16-bit value broadcast on Block A of every RDS group; it
      /// does NOT rotate like PS. The argument is the raw PI code; the call
      /// sign decode lives in the Infrastructure layer
      /// (<c>RbdsCallSignDecoder</c>) so RTLSDRCore stays a pure-DSP
      /// concern without knowledge of FCC call-sign rules.
      /// </remarks>
      public event EventHandler<ushort>? ProgramIdChanged;

      /// <summary>
      /// Creates a new radio receiver with the specified device
      /// </summary>
      /// <param name="device">SDR device to use</param>
      public RadioReceiver(ISdrDevice device)
          : this(device, null)
      {
      }

      /// <summary>
      /// Creates a new radio receiver with the specified device and logger
      /// </summary>
      /// <param name="device">SDR device to use</param>
      /// <param name="logger">Logger; the global Serilog logger with this type's context when null.</param>
      public RadioReceiver(ISdrDevice device, ILogger? logger)
      {
          Logger = logger ?? Log.ForContext<RadioReceiver>();
          _device = device ?? throw new ArgumentNullException(nameof(device));
          _currentBand = BandPresets.FmBroadcast;
          _currentFrequencyHz = _currentBand.CenterFrequencyHz;
          _currentModulation = _currentBand.DefaultModulation;
          _demodulator = DemodulatorFactory.Create(_currentModulation);

          Logger.Information("RadioReceiver created with device: {DeviceName}", device.DeviceInfo.Name);
      }

      /// <summary>
      /// Creates a new radio receiver with a mock device
      /// </summary>
      public static RadioReceiver CreateWithMockDevice()
      {
          var mockDevice = SdrDeviceFactory.CreateMockDevice();
          return new RadioReceiver(mockDevice);
      }

      /// <summary>
      /// Creates a new radio receiver with the first available device
      /// </summary>
      public static RadioReceiver? CreateWithFirstAvailableDevice() =>
          CreateWithFirstAvailableDevice(HfDirectSamplingSetting.Auto);

      /// <summary>
      /// Creates a new radio receiver with the first available device
      /// </summary>
      /// <param name="hfSetting">The <c>RTLSDR:HfDirectSampling</c> setting for the device (AUD-99).</param>
      public static RadioReceiver? CreateWithFirstAvailableDevice(HfDirectSamplingSetting hfSetting)
      {
          var device = SdrDeviceFactory.CreateFirstAvailable(hfSetting);
          return device != null ? new RadioReceiver(device) : null;
      }

      /// <summary>
      /// Gets the device's information: product, tuner, HF input and frequency range (AUD-99).
      /// </summary>
      public DeviceInfo DeviceInfo => _device.DeviceInfo;

      /// <summary>
      /// Gets whether the device is in HF direct-sampling mode now. False while the device is closed.
      /// </summary>
      public bool IsHfMode => _device.IsHfMode;

      /// <summary>
      /// Puts the open device in the mode <paramref name="band"/> needs (<see cref="HfBands.Requires(BandType)"/>):
      /// HF direct sampling for AM and Shortwave, the tuner for every other band. Does nothing when
      /// the device is already in that mode. Must be called with streaming stopped and before the
      /// centre frequency for <paramref name="band"/> is set.
      /// </summary>
      /// <remarks>
      /// <para>
      /// When the switch is to the tuner, the device is first tuned to <paramref name="nextFrequencyHz"/>
      /// while direct sampling is still on. In that mode librtlsdr's <c>rtlsdr_set_center_freq</c> only
      /// programs the RTL2832's IF/DDC; if the HF-off switch is then refused, the device is tuned back
      /// to the receiver's current frequency, so the pre-tune leaves nothing behind. Its purpose is that librtlsdr's own HF-off
      /// branch of <c>rtlsdr_set_direct_sampling</c> ends with <c>rtlsdr_set_center_freq(dev, dev-&gt;freq)</c>,
      /// and without the pre-tune <c>dev-&gt;freq</c> is still the HF frequency, which the R820T cannot
      /// lock to: it prints <c>[R82XX] PLL not locked!</c> on every AM/SW → FM switch. With it, that
      /// retune lands on the new frequency. A false result from the pre-tune is logged at Debug and
      /// does not fail the switch; the caller's normal tune follows.
      /// </para>
      /// <para>
      /// Whenever a switch was attempted and the device is in tuner mode afterwards, the gain mode
      /// (and the manual gain when auto gain is off) is applied again, as <see cref="Startup"/> does.
      /// librtlsdr's HF-off branch calls the tuner's <c>init</c>, which rewrites the tuner's
      /// registers, gain included, to their defaults; without this, FM reached from AM ran at a
      /// different gain from FM after a cold start and a user's manual gain was lost.
      /// </para>
      /// </remarks>
      /// <param name="band">The band about to be received.</param>
      /// <param name="nextFrequencyHz">The frequency the caller tunes next.</param>
      /// <returns>
      /// <see cref="TuneResult.Success"/> when the device is in the mode <paramref name="band"/>
      /// needs; <see cref="TuneResult.Unsupported"/> when the band needs HF direct sampling and the
      /// device has none; <see cref="TuneResult.Refused"/> when the device refused the switch.
      /// </returns>
      private TuneResult ApplyHfModeFor(RadioBand band, long nextFrequencyHz)
      {
          bool wanted = HfBands.Requires(band.Type);
          if (_device.IsHfMode == wanted)
          {
              return TuneResult.Success;
          }

          if (wanted && !_device.SupportsHfDirectSampling)
          {
              Logger.Warning(
                  "The {Band} band needs HF direct sampling, which this device does not have (product {Product}; see RTLSDR:HfDirectSampling)",
                  band.Name, _device.DeviceInfo.Product);
              return TuneResult.Unsupported;
          }

          bool switched;
          lock (_tuneLock)
          {
              if (!wanted && !_device.SetFrequency(DeviceFrequencyForBand(nextFrequencyHz, band)))
              {
                  Logger.Debug("Pre-tune to {Frequency} before switching HF direct sampling off was refused",
                      RadioBand.FormatFrequency(nextFrequencyHz));
              }

              switched = _device.SetHfMode(wanted);

              if (!_device.IsHfMode)
              {
                  ApplyGainLocked();
              }
              else if (!wanted)
              {
                  // HF off was refused and the device is still in direct sampling, but the pre-tune
                  // above moved its IF/DDC to nextFrequencyHz. The caller keeps the receiver on
                  // _currentFrequencyHz, so put the device back there too; otherwise it would play a
                  // frequency the receiver does not report.
                  if (!_device.SetFrequency(DeviceFrequency(_currentFrequencyHz)))
                  {
                      Logger.Debug("Re-tune to {Frequency} after a refused HF-off switch was refused",
                          RadioBand.FormatFrequency(_currentFrequencyHz));
                  }
              }
          }

          if (!switched)
          {
              Logger.Warning("The device refused to switch HF direct sampling {State} for the {Band} band",
                  wanted ? "on" : "off", band.Name);
              return TuneResult.Refused;
          }

          return TuneResult.Success;
      }

      /// <summary>
      /// Applies the recorded gain mode, and the manual gain when auto gain is off, to the device.
      /// Call with <see cref="_tuneLock"/> held.
      /// </summary>
      private void ApplyGainLocked()
      {
          _device.SetGainMode(_autoGain);
          if (!_autoGain)
          {
              _device.SetGain(_manualGain);
          }
      }

      /// <summary>
      /// The frequency the device is tuned to so that the receiver, with the DSP chain it has set up
      /// now, receives <paramref name="frequencyHz"/> (AUD-101): 60 kHz below it while the AM chain
      /// demodulates (<see cref="AmReceiveChain.DeviceFrequencyFor"/>), the frequency itself
      /// otherwise. Every tune the receiver makes for the radio goes through this or
      /// <see cref="DeviceFrequencyForBand"/>; the live band sweep's own hops do not.
      /// </summary>
      private long DeviceFrequency(long frequencyHz) =>
          AmReceiveChain.DeviceFrequencyFor(frequencyHz, _amChain != null);

      /// <summary>
      /// As <see cref="DeviceFrequency"/>, for <paramref name="band"/> before its chain is set up:
      /// offset when <paramref name="band"/>'s modulation and bandwidth select the AM chain
      /// (<see cref="AmReceiveChain.Applies"/>), the rule <see cref="SetupSignalProcessing"/> uses.
      /// </summary>
      private static long DeviceFrequencyForBand(long frequencyHz, RadioBand band) =>
          AmReceiveChain.DeviceFrequencyFor(
              frequencyHz, AmReceiveChain.Applies(band.DefaultModulation, band.DefaultBandwidthHz));

      /// <summary>
      /// Runs a start-up configuration step, and runs it once more if the device refuses it the
      /// first time (AUD-99). There is no wait between the two attempts.
      /// </summary>
      /// <returns>The second attempt's result when the first was refused.</returns>
      private bool WithOneRetry(Func<bool> step, string what)
      {
          if (step())
          {
              return true;
          }

          Logger.Warning("The device refused {What} at start-up; trying once more", what);
          return step();
      }

      #region IRadioControl Lifecycle Implementation

      /// <inheritdoc/>
      public bool Startup()
      {
          lock (_lock)
          {
              if (_state == ReceiverState.Running)
              {
                  Logger.Warning("Receiver is already running");
                  return false;
              }

              try
              {
                  SetState(ReceiverState.Starting);
                  Logger.Information("Starting receiver on {Band} at {Frequency}",
                      _currentBand.Name, RadioBand.FormatFrequency(_currentFrequencyHz));

                  if (!_device.IsOpen && !_device.Open())
                  {
                      Logger.Error("Failed to open device");
                      SetState(ReceiverState.Error);
                      return false;
                  }

                  // AUD-99: a saved AM/SW band on a device without HF direct
                  // sampling starts on FM instead of failing the start.
                  if (HfBands.Requires(_currentBand.Type) && !_device.SupportsHfDirectSampling)
                  {
                      FallBackToFmForStartup();
                  }

                  // Setup signal processing chain first (calculates the
                  // appropriate SDR sample rate for the current modulation)
                  _pendingFmStart = new FmChainStart("start-up", ByRecovery: false);
                  SetupSignalProcessing();

                  // AUD-99: the HF mode first (AM/SW on, every other band off),
                  // then the modulation-appropriate sample rate, then the
                  // frequency. A refusal of any of the three fails the start
                  // (the sample rate and the tune are tried twice first):
                  // a receiver that streams from a device that is not where
                  // it reports being is worse than one that says it failed.
                  if (ApplyHfModeFor(_currentBand, _currentFrequencyHz) != TuneResult.Success)
                  {
                      Logger.Error("Receiver start failed: the HF mode for the {Band} band was refused", _currentBand.Name);
                      CloseDeviceAfterFailedStartup();
                      SetState(ReceiverState.Error);
                      return false;
                  }

                  if (!WithOneRetry(() => _device.SetSampleRate(_sdrSampleRate), "the sample rate"))
                  {
                      Logger.Error("Receiver start failed: the device refused the sample rate {SampleRate} Hz", _sdrSampleRate);
                      CloseDeviceAfterFailedStartup();
                      SetState(ReceiverState.Error);
                      return false;
                  }

                  bool tuned = WithOneRetry(() =>
                  {
                      lock (_tuneLock)
                      {
                          return _device.SetFrequency(DeviceFrequency(_currentFrequencyHz));
                      }
                  }, "the first tune");

                  if (!tuned)
                  {
                      Logger.Error("Receiver start failed: the device refused {Frequency}",
                          RadioBand.FormatFrequency(_currentFrequencyHz));
                      CloseDeviceAfterFailedStartup();
                      SetState(ReceiverState.Error);
                      return false;
                  }

                  // Gain is applied after the frequency, as before. In HF mode the
                  // device makes these calls no-ops (the tuner is in standby).
                  _device.SetGainMode(_autoGain);
                  if (!_autoGain)
                  {
                      _device.SetGain(_manualGain);
                  }

                  // Start streaming
                  if (!_device.StartStreaming())
                  {
                      Logger.Error("Failed to start streaming");
                      CloseDeviceAfterFailedStartup();
                      SetState(ReceiverState.Error);
                      return false;
                  }

                  // Start async processing pipeline: USB read thread enqueues
                  // IQ batches, dedicated processing thread runs DSP chain.
                  _processingCts = new CancellationTokenSource();
                  _processingQueue = new BlockingCollection<IqBlock>(
                      new ConcurrentQueue<IqBlock>(), boundedCapacity: 8);
                  _processingThread = new Thread(ProcessingLoop)
                  {
                      Name = "RadioReceiver-DSP",
                      IsBackground = true,
                      Priority = ThreadPriority.AboveNormal
                  };
                  _processingThread.Start();
                  _device.SamplesAvailable += OnSamplesAvailable;

                  SetState(ReceiverState.Running);
                  Logger.Information("Receiver started successfully");
                  return true;
              }
              catch (Exception ex)
              {
                  Logger.Error(ex, "Error starting receiver");
                  CloseDeviceAfterFailedStartup();
                  SetState(ReceiverState.Error);
                  return false;
              }
          }
      }

      /// <summary>
      /// Moves the receiver, before it starts, from an HF band the device cannot receive to the FM
      /// band at its centre frequency, with one Warning (AUD-99). Called by <see cref="Startup"/>
      /// with <see cref="_lock"/> held.
      /// </summary>
      private void FallBackToFmForStartup()
      {
          RadioBand saved = _currentBand;
          RadioBand fm = BandPresets.GetBand(BandType.FM);
          long oldFrequency = _currentFrequencyHz;

          _currentBand = fm;
          _currentModulation = fm.DefaultModulation;
          _currentFrequencyHz = fm.CenterFrequencyHz;

          Logger.Warning(
              "The {Band} band needs HF direct sampling, which this device does not have (product {Product}; see RTLSDR:HfDirectSampling); starting on {Fallback} at {Frequency} instead",
              saved.Name, _device.DeviceInfo.Product, fm.Name, RadioBand.FormatFrequency(_currentFrequencyHz));

          if (oldFrequency != _currentFrequencyHz)
          {
              FrequencyChanged?.Invoke(this, new FrequencyChangedEventArgs(oldFrequency, _currentFrequencyHz));
          }
      }

      /// <summary>
      /// Stops streaming and closes the device after <see cref="Startup"/>
      /// failed with the device possibly open, so a failed start never
      /// leaves the dongle held (AUD-76: the device gate is released on
      /// failure and an idle band sweep may then open the device).
      /// </summary>
      private void CloseDeviceAfterFailedStartup()
      {
          _device.SamplesAvailable -= OnSamplesAvailable;
          _processingCts?.Cancel();
          _processingQueue?.CompleteAdding();
          _processingThread?.Join(TimeSpan.FromSeconds(2));
          _processingQueue?.Dispose();
          _processingQueue = null;
          _processingThread = null;
          _processingCts?.Dispose();
          _processingCts = null;

          try
          {
              lock (_tuneLock)
              {
                  if (_device.IsStreaming)
                  {
                      _device.StopStreaming();
                  }
                  if (_device.IsOpen)
                  {
                      _device.Close();
                  }
              }
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error closing the device after a failed receiver start");
          }
      }

      /// <inheritdoc/>
      public void Shutdown()
      {
          lock (_lock)
          {
              if (_state == ReceiverState.Stopped)
      {
        return;
      }

      // Cancel any ongoing scan
      CancelScan();

              // Stopping is published before the sweep is cancelled.
              // SweepChannels registers under _sweepStateLock only while the
              // state is Running, and CancelSweep takes that same lock, so a
              // sweep either registered before this cancel (and is cancelled)
              // or sees Stopping and refuses to start.
              SetState(ReceiverState.Stopping);
              Logger.Information("Shutting down receiver");

              // Cancel any live band sweep and wait (bounded) for its
              // cleanup to finish before the device is closed. The sweep
              // never takes _lock, so waiting here while holding it cannot
              // deadlock against the sweep's finally block.
              CancelSweep();
              if (!_sweepIdle.Wait(SweepIdleWaitTimeout))
              {
                  Logger.Warning("Band sweep did not finish within {Timeout}s of cancellation; shutting down anyway",
                      SweepIdleWaitTimeout.TotalSeconds);
              }

              _device.SamplesAvailable -= OnSamplesAvailable;
              _processingCts?.Cancel();

              // Signal the processing queue to complete, then wait for
              // the processing thread to drain remaining items and exit.
              _processingQueue?.CompleteAdding();
              _processingThread?.Join(TimeSpan.FromSeconds(2));
              _processingQueue?.Dispose();
              _processingQueue = null;
              _processingThread = null;

              _processingCts?.Dispose();
              _processingCts = null;

              lock (_tuneLock)
              {
                  _device.StopStreaming();
                  _device.Close();
              }

              SetState(ReceiverState.Stopped);
              Logger.Information("Receiver shutdown complete");
          }
      }

      /// <inheritdoc/>
      public bool IsRunning => _state == ReceiverState.Running || _state == ReceiverState.Scanning;

      #endregion

      #region IRadioControl Frequency Implementation

      /// <inheritdoc/>
      public long CurrentFrequency => _currentFrequencyHz;

      /// <inheritdoc/>
      /// <remarks>
      /// <see cref="TrySetFrequency"/> says why a false result was false.
      /// </remarks>
      public bool SetFrequency(long frequencyHz) => TrySetFrequency(frequencyHz) == TuneResult.Success;

      /// <summary>
      /// Tunes to <paramref name="frequencyHz"/>, switching to the preset band that contains it
      /// (<see cref="SetBand"/>) or, when none does, to a custom band around it, and reports how it went.
      /// </summary>
      /// <remarks>
      /// A custom band is received with the tuner (HF direct sampling off), so its lower limit is
      /// <see cref="RtlSdrDevice.TunerMinFrequencyHz"/> whatever the device's HF support (AUD-99).
      /// </remarks>
      /// <param name="frequencyHz">Frequency, in Hz.</param>
      /// <returns>
      /// <see cref="TuneResult.OutOfRange"/> when no preset band contains the frequency and it is
      /// outside what the device can receive without HF; nothing is changed then.
      /// <see cref="TuneResult.Refused"/> when the device refused the HF switch, the sample rate or
      /// the frequency. <see cref="TuneResult.Unsupported"/> when the frequency is in AM or
      /// Shortwave and the open device has no HF direct sampling (<see cref="TrySetBand"/>).
      /// </returns>
      public TuneResult TrySetFrequency(long frequencyHz)
      {
          // Try to find an appropriate band for this frequency
          var band = BandPresets.FindBandForFrequency(frequencyHz);

          if (band != null)
          {
              // Switch to the appropriate band. TrySetBand rebuilds the chain even inside FM, so the
              // watchdog logs this as a frequency tune, not a band change (AUD-106).
              _pendingFmStart = new FmChainStart("frequency tune", ByRecovery: false);
              return TrySetBand(band.Type, frequencyHz);
          }

          // No matching band found - check if within the range the tuner path receives
          long minimumHz = Math.Max(_device.DeviceInfo.MinFrequencyHz, RtlSdrDevice.TunerMinFrequencyHz);
          if (frequencyHz < minimumHz ||
              frequencyHz > _device.DeviceInfo.MaxFrequencyHz)
          {
              Logger.Warning("Frequency {Frequency} is outside device range",
                  RadioBand.FormatFrequency(frequencyHz));
              return TuneResult.OutOfRange;
          }

          // Create a custom band centered on this frequency
          var customBand = BandPresets.CreateCustomBand(
              "Custom",
              frequencyHz - 1_000_000,
              frequencyHz + 1_000_000,
              ModulationType.NFM);

          // A user tune wins over a live band sweep; cancelled before streaming may pause.
          CancelSweep();

          lock (_lock)
          {
              bool wasStreaming = false;
              bool configured = true;
              if (_device.IsOpen && _device.IsHfMode)
              {
                  // Leaving AM/SW for a custom band: HF off with streaming paused, then the sample
                  // rate and the tune, and only then is streaming resumed.
                  wasStreaming = _device.IsStreaming;
                  if (wasStreaming)
                  {
                      _device.StopStreaming();
                  }

                  if (ApplyHfModeFor(customBand, frequencyHz) != TuneResult.Success)
                  {
                      ResumeAfterBandChange(wasStreaming);
                      return TuneResult.Refused;
                  }

                  // librtlsdr's HF-off branch runs the tuner's init, which resets the tuner's IF
                  // filter; setting the sample rate again sets the filter for it.
                  if (!_device.SetSampleRate(_sdrSampleRate))
                  {
                      Logger.Warning("The device refused the sample rate {SampleRate} Hz after switching HF direct sampling off",
                          _sdrSampleRate);
                      configured = false;
                  }
              }

              _currentBand = customBand;
              bool tuned = SetFrequencyInternal(frequencyHz);
              bool resumed = ResumeAfterBandChange(wasStreaming);
              return tuned && configured && resumed ? TuneResult.Success : TuneResult.Refused;
          }
      }

      /// <inheritdoc/>
      public bool SetFrequencyInBand(long frequencyHz)
      {
          if (!_currentBand.ContainsFrequency(frequencyHz))
          {
              throw new ArgumentOutOfRangeException(nameof(frequencyHz),
                  $"Frequency {RadioBand.FormatFrequency(frequencyHz)} is outside current band " +
                  $"({RadioBand.FormatFrequency(_currentBand.MinFrequencyHz)} - " +
                  $"{RadioBand.FormatFrequency(_currentBand.MaxFrequencyHz)})");
          }

          return SetFrequencyInternal(frequencyHz);
      }

      /// <summary>
      /// The frequency a scan wraps to at a band edge. On the HF bands (AM, Shortwave) the edge is
      /// moved onto the step's grid, inward: Shortwave starts at 1711 kHz (AUD-102), one above AM's
      /// top channel, and wrapping to it would put every later 5 kHz step off the broadcast grid
      /// (1716, 1721, ...). On every other band the edge itself is returned, as before: FM's grid is
      /// not anchored at zero (US channels are on odd tenths), so rounding there would be wrong.
      /// </summary>
      internal static long ScanWrapTarget(RadioBand band, long edgeHz, long stepHz, bool roundUp)
      {
          if (stepHz <= 0 || !HfBands.Requires(band.Type))
          {
              return edgeHz;
          }

          long down = edgeHz / stepHz * stepHz;
          long target = roundUp && down < edgeHz ? down + stepHz : down;
          return band.ContainsFrequency(target) ? target : edgeHz;
      }

      /// <inheritdoc/>
      public bool TuneFrequencyUp(long stepHz = 100_000)
      {
          var newFrequency = _currentFrequencyHz + stepHz;
          if (newFrequency > _currentBand.MaxFrequencyHz)
          {
              Logger.Debug("At upper band limit");
              return false;
          }

          return SetFrequencyInternal(newFrequency);
      }

      /// <inheritdoc/>
      public bool TuneFrequencyDown(long stepHz = 100_000)
      {
          var newFrequency = _currentFrequencyHz - stepHz;
          if (newFrequency < _currentBand.MinFrequencyHz)
          {
              Logger.Debug("At lower band limit");
              return false;
          }

          return SetFrequencyInternal(newFrequency);
      }

      /// <summary>
      /// Records <paramref name="frequencyHz"/> as the receiver's frequency and, when the device is
      /// open, tunes the device to it.
      /// </summary>
      /// <remarks>
      /// The field is updated and <see cref="FrequencyChanged"/> raised even when the device refuses
      /// the tune, as before AUD-99; the refusal is logged at Warning and reported by the return value.
      /// </remarks>
      /// <returns>The device's result, or true when the device is closed (the next start tunes it).</returns>
      private bool SetFrequencyInternal(long frequencyHz)
      {
          var oldFrequency = _currentFrequencyHz;
          _currentFrequencyHz = frequencyHz;

          // A user tune wins over a live band sweep. The field is assigned
          // first so the sweep's cleanup, which retunes to whatever
          // _currentFrequencyHz holds when it runs, lands on this frequency.
          CancelSweep();

          bool success = true;
          if (_device.IsOpen)
          {
              // rtlsdr_set_center_freq is safe to call while streaming —
              // no need to stop/restart the USB pipe for frequency changes.
              // AUD-101: on AM-modulated bands the device is tuned below the frequency (see
              // DeviceFrequency); the receiver still records and reports frequencyHz.
              long deviceHz = DeviceFrequency(frequencyHz);
              lock (_tuneLock)
              {
                  success = _device.SetFrequency(deviceHz);
              }
              Interlocked.Increment(ref _tuneGeneration);

              if (!success)
              {
                  Logger.Warning("The device refused {Frequency} (device centre {DeviceFrequency}, band {Band}, HF direct sampling {HfMode})",
                      RadioBand.FormatFrequency(frequencyHz), RadioBand.FormatFrequency(deviceHz), _currentBand.Name,
                      _device.IsHfMode ? "on" : "off");
              }
          }

          if (oldFrequency != frequencyHz)
          {
              _rdsDecoder?.Reset();
              FrequencyChanged?.Invoke(this, new FrequencyChangedEventArgs(oldFrequency, frequencyHz));
          }

          Logger.Debug("Frequency set to {Frequency}", RadioBand.FormatFrequency(frequencyHz));
          return success;
      }

      #endregion

      #region IRadioControl Scanning Implementation

      /// <inheritdoc/>
      public bool IsScanning => _isScanning;

      /// <inheritdoc/>
      public bool ScanFrequencyUp(long stepHz = 100_000, float signalThreshold = 0.3f, int dwellTimeMs = 150)
      {
          return ScanInternal(stepHz, signalThreshold, dwellTimeMs, ascending: true);
      }

      /// <inheritdoc/>
      public bool ScanFrequencyDown(long stepHz = 100_000, float signalThreshold = 0.3f, int dwellTimeMs = 150)
      {
          return ScanInternal(stepHz, signalThreshold, dwellTimeMs, ascending: false);
      }

      private bool ScanInternal(long stepHz, float signalThreshold, int dwellTimeMs, bool ascending)
      {
          if (_state != ReceiverState.Running && _state != ReceiverState.Scanning)
          {
              throw new InvalidOperationException("Receiver must be started before scanning");
          }

          var direction = ascending ? "up" : "down";
          Logger.Information("Starting continuous scan {Direction} from {Frequency} (threshold={Threshold:F2}, dwell={Dwell}ms)",
              direction, RadioBand.FormatFrequency(_currentFrequencyHz), signalThreshold, dwellTimeMs);

          _scanCts = new CancellationTokenSource();
          _isScanning = true;
          SetState(ReceiverState.Scanning);

          // A seek scan cancels a live band sweep. This runs after
          // _isScanning is set, and SweepChannels refuses to start while it
          // is set, so a new sweep cannot slip in behind this cancel.
          CancelSweep();

          try
          {
              // Track start frequency to detect full-sweep completion
              var startFrequency = _currentFrequencyHz;

              while (!_scanCts.Token.IsCancellationRequested)
              {
                  // Calculate next frequency with band-edge wrap-around
                  var nextFreq = ascending
                      ? _currentFrequencyHz + stepHz
                      : _currentFrequencyHz - stepHz;

                  if (ascending && nextFreq > _currentBand.MaxFrequencyHz)
                  {
                      nextFreq = ScanWrapTarget(_currentBand, _currentBand.MinFrequencyHz, stepHz, roundUp: true);
                      Logger.Debug("Scan wrapped from upper to lower band edge");
                  }
                  else if (!ascending && nextFreq < _currentBand.MinFrequencyHz)
                  {
                      nextFreq = ScanWrapTarget(_currentBand, _currentBand.MaxFrequencyHz, stepHz, roundUp: false);
                      Logger.Debug("Scan wrapped from lower to upper band edge");
                  }

                  // Full-sweep auto-stop: we've returned to where we started
                  if (nextFreq == startFrequency ||
                      (ascending && _currentFrequencyHz < startFrequency && nextFreq >= startFrequency) ||
                      (!ascending && _currentFrequencyHz > startFrequency && nextFreq <= startFrequency))
                  {
                      Logger.Information("Scan completed full sweep, returning to start {Frequency}",
                          RadioBand.FormatFrequency(startFrequency));
                      break;
                  }

                  SetFrequencyInternal(nextFreq);

                  // Dwell to allow USB reads and signal strength computation.
                  // At 240kHz SDR rate with 16384 IQ pairs per USB transfer,
                  // each transfer takes ~68ms. 150ms dwell ensures at least
                  // 2 full reads for a reliable signal strength measurement.
                  // Use WaitHandle so the dwell is cancellable via _scanCts.
                  if (_scanCts.Token.WaitHandle.WaitOne(dwellTimeMs))
        {
          break; // Cancelled during dwell
        }

        float strength = _lastSignalStrength;
        // AUD-103: a frequency the caller marks as never a station (a known device spur) is
        // stepped past whatever it reads: no pause, no SeekStationFound.
        if (strength >= signalThreshold && IsSeekSkipped(_currentFrequencyHz))
        {
          Logger.Debug("Seek passed {Frequency} (strength {Strength:F3}): not a station (SeekSkips)",
              RadioBand.FormatFrequency(_currentFrequencyHz), strength);
        }
        else if (strength >= signalThreshold)
                  {
                      Logger.Information("Signal found at {Frequency} (strength: {Strength:F3}, threshold: {Threshold:F2}), pausing {PauseMs}ms",
                          RadioBand.FormatFrequency(_currentFrequencyHz), strength, signalThreshold, ScanPauseMs);

                      RaiseSeekStationFound(_currentFrequencyHz, strength);

                      // Pause on signal so user can listen.
                      // WaitHandle.WaitOne returns true if signaled (cancelled),
                      // false on timeout — so scan resumes after the pause.
                      if (_scanCts.Token.WaitHandle.WaitOne(ScanPauseMs))
          {
            break; // User hit STOP during pause
          }
        }
              }

              Logger.Information("Scan stopped at {Frequency}", RadioBand.FormatFrequency(_currentFrequencyHz));
              SetState(ReceiverState.Running);
              _isScanning = false;
              return false;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error during scan");
              SetState(ReceiverState.Running);
              _isScanning = false;
              throw;
          }
          finally
          {
              _scanCts?.Dispose();
              _scanCts = null;
          }
      }

      /// <summary>
      /// Hops between <paramref name="stationsHz"/> in one direction, pausing
      /// <see cref="ScanPauseMs"/> on each so the listener can hear it, until
      /// <see cref="CancelScan"/>, until the next hop would reach or pass the
      /// frequency the scan started on, or until the band changes (AUD-100);
      /// never more hops than there are stations. The station-list twin of
      /// <see cref="ScanFrequencyUp"/> / <see cref="ScanFrequencyDown"/>: it
      /// tunes straight from station to station, with no stepping through
      /// the frequencies between, and it measures nothing — every station it
      /// lands on gets its pause, whatever its signal is now. <see cref="SeekStationFound"/> is not raised.
      /// Blocks the calling thread.
      /// </summary>
      /// <param name="stationsHz">
      /// Station frequencies, in any order. Those outside the current band are ignored.
      /// </param>
      /// <param name="ascending">True for Scan Up.</param>
      /// <param name="minGapHz">See <see cref="StationListScan.Next"/>.</param>
      /// <returns>Always false, as the live seek: the scan ends by cancellation or by going round.</returns>
      /// <exception cref="InvalidOperationException">The receiver is not started.</exception>
      public bool ScanStations(IReadOnlyList<long> stationsHz, bool ascending, long minGapHz)
      {
          ArgumentNullException.ThrowIfNull(stationsHz);
          if (_state != ReceiverState.Running && _state != ReceiverState.Scanning)
          {
              throw new InvalidOperationException("Receiver must be started before scanning");
          }

          RadioBand band = _currentBand;
          long[] stations = stationsHz
              .Where(band.ContainsFrequency)
              .Distinct()
              .OrderBy(hz => hz)
              .ToArray();

          Logger.Information("Starting station scan {Direction} from {Frequency} over {Count} stations",
              ascending ? "up" : "down", RadioBand.FormatFrequency(_currentFrequencyHz), stations.Length);

          _scanCts = new CancellationTokenSource();
          _isScanning = true;
          SetState(ReceiverState.Scanning);

          // As in ScanInternal: cancel a live band sweep after _isScanning is
          // set, so a new sweep cannot slip in behind this cancel.
          CancelSweep();

          try
          {
              long startFrequency = _currentFrequencyHz;
              // Backstop: a scan round visits each station at most once, so more hops than
              // stations means the circle check has missed and the scan would never end.
              int hops = 0;
              while (!_scanCts.Token.IsCancellationRequested)
              {
                  // The list was filtered against the band the scan started on. A band change
                  // mid-scan (SetBand does not cancel a scan) ends it rather than tuning the
                  // old band's stations inside the new band.
                  if (!ReferenceEquals(_currentBand, band))
                  {
                      Logger.Information("Station scan ended: the band changed to {Band}", _currentBand.Name);
                      break;
                  }

                  if (hops >= stations.Length)
                  {
                      Logger.Warning("Station scan ended after {Hops} hops without going round; stopping", hops);
                      break;
                  }

                  long? next = StationListScan.Next(stations, _currentFrequencyHz, ascending, minGapHz);
                  if (next == null)
                  {
                      Logger.Information("Station scan has no other station to go to");
                      break;
                  }

                  if (StationListScan.CompletesCircle(startFrequency, _currentFrequencyHz, next.Value, ascending, minGapHz))
                  {
                      Logger.Information("Station scan went all the way round from {Frequency}",
                          RadioBand.FormatFrequency(startFrequency));
                      break;
                  }

                  hops++;
                  SetFrequencyInternal(next.Value);
                  Logger.Information("Station scan stopped at {Frequency}, pausing {PauseMs}ms",
                      RadioBand.FormatFrequency(next.Value), ScanPauseMs);

                  if (_scanCts.Token.WaitHandle.WaitOne(ScanPauseMs))
                  {
                      break; // User hit STOP during the pause
                  }
              }

              Logger.Information("Scan stopped at {Frequency}", RadioBand.FormatFrequency(_currentFrequencyHz));
              SetState(ReceiverState.Running);
              _isScanning = false;
              return false;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error during station scan");
              SetState(ReceiverState.Running);
              _isScanning = false;
              throw;
          }
          finally
          {
              _scanCts?.Dispose();
              _scanCts = null;
          }
      }

      /// <summary>
      /// How long a scan rests on each station it stops on, in milliseconds,
      /// before moving on. Default 2000. Applies to the live seek and to
      /// <see cref="ScanStations"/>.
      /// </summary>
      public int ScanPauseMs { get; set; } = 2000;

      /// <summary>
      /// Frequencies a live seek (<see cref="ScanFrequencyUp"/>, <see cref="ScanFrequencyDown"/>)
      /// must never stop on, however strong they read (AUD-103): true for a frequency that is not a
      /// station. The seek steps past such a frequency without pausing and without raising
      /// <see cref="SeekStationFound"/>. Null, the default, skips nothing. The receiver keeps no list
      /// of its own; the caller supplies one. A predicate that throws is logged at Warning and the
      /// frequency is treated as not skipped.
      /// </summary>
      public Func<long, bool>? SeekSkips { get; set; }

      private bool IsSeekSkipped(long frequencyHz)
      {
          Func<long, bool>? skips = SeekSkips;
          if (skips == null)
          {
              return false;
          }

          try
          {
              return skips(frequencyHz);
          }
          catch (Exception ex)
          {
              Logger.Warning(ex, "The SeekSkips predicate threw at {Frequency}; treating it as a station candidate",
                  RadioBand.FormatFrequency(frequencyHz));
              return false;
          }
      }

      /// <summary>
      /// Raised by a live seek (<see cref="ScanFrequencyUp"/>,
      /// <see cref="ScanFrequencyDown"/>) each time it stops on a signal, on
      /// the scan's thread, before its pause (AUD-100). Not raised by
      /// <see cref="ScanStations"/>. An exception thrown by a handler is
      /// logged and does not end the scan.
      /// </summary>
      public event EventHandler<SeekStationFoundEventArgs>? SeekStationFound;

      private void RaiseSeekStationFound(long frequencyHz, float strength)
      {
          try
          {
              SeekStationFound?.Invoke(this, new SeekStationFoundEventArgs(frequencyHz, strength));
          }
          catch (Exception ex)
          {
              Logger.Warning(ex, "A SeekStationFound handler threw; the scan continues");
          }
      }

      /// <inheritdoc/>
      public void CancelScan()
      {
          if (_isScanning && _scanCts != null)
          {
              Logger.Information("Cancelling scan");
              _scanCts.Cancel();
          }
      }

      #endregion

      #region IRadioControl Band and Modulation Implementation

      /// <inheritdoc/>
      /// <remarks>
      /// <see cref="TrySetBand"/> says why a false result was false.
      /// </remarks>
      public bool SetBand(BandType bandType, long? specificFrequency = null) =>
          TrySetBand(bandType, specificFrequency) == TuneResult.Success;

      /// <summary>
      /// Switches to <paramref name="bandType"/> at <paramref name="specificFrequency"/> (clamped into
      /// the band), or at the current frequency clamped into the band, and reports how it went.
      /// </summary>
      /// <remarks>
      /// With the device open, streaming is paused, the device is put in the band's HF mode
      /// (<see cref="HfBands.Requires(BandType)"/>; AUD-99; leaving HF mode also re-applies the gain
      /// and pre-tunes to the new frequency, see <see cref="ApplyHfModeFor"/>), and then the sample
      /// rate and the frequency are set, before streaming resumes. A refused or unsupported HF switch
      /// leaves the receiver on its previous band and frequency, with streaming resumed; a refused
      /// sample rate or frequency leaves it on the new band, at the new frequency, with streaming
      /// resumed. With the device closed only the band and frequency are recorded, for the next start.
      /// </remarks>
      /// <param name="bandType">The band.</param>
      /// <param name="specificFrequency">The frequency, in Hz; the current frequency when null.</param>
      /// <returns>
      /// <see cref="TuneResult.Success"/>; <see cref="TuneResult.Unsupported"/> when the band needs HF
      /// direct sampling and the open device has none; <see cref="TuneResult.Refused"/> when the
      /// device refused the HF switch, the sample rate or the frequency, streaming could not be
      /// resumed, or the switch threw.
      /// </returns>
      public TuneResult TrySetBand(BandType bandType, long? specificFrequency = null)
      {
          // A band change cancels a live band sweep before streaming is paused.
          CancelSweep();

          lock (_lock)
          {
              try
              {
                  var band = BandPresets.GetBand(bandType);
                  var newFrequency = band.ClampFrequency(specificFrequency ?? _currentFrequencyHz);
                  var wasStreaming = _device.IsStreaming;

                  Logger.Information("Switching to {Band}{Pause}", band.Name,
                      wasStreaming ? " (pausing stream)" : "");

                  // Stop streaming before tuning to avoid USB pipe stalls
                  if (wasStreaming)
                  {
                      _device.StopStreaming();
                  }

                  // The HF switch comes before anything about the band is recorded, so a refusal
                  // leaves the receiver where it was.
                  if (_device.IsOpen)
                  {
                      TuneResult hf = ApplyHfModeFor(band, newFrequency);
                      if (hf != TuneResult.Success)
                      {
                          ResumeAfterBandChange(wasStreaming);
                          return hf;
                      }
                  }

                  _currentBand = band;
                  _currentModulation = band.DefaultModulation;

                  bool configured = true;
                  if (_state == ReceiverState.Running || _state == ReceiverState.Scanning)
                  {
                      SetupSignalProcessing();
                      if (!_device.SetSampleRate(_sdrSampleRate))
                      {
                          Logger.Warning("The device refused the sample rate {SampleRate} Hz for the {Band} band",
                              _sdrSampleRate, band.Name);
                          configured = false;
                      }
                  }

                  bool tuned = SetFrequencyInternal(newFrequency);

                  bool resumed = ResumeAfterBandChange(wasStreaming);
                  return resumed && configured && tuned ? TuneResult.Success : TuneResult.Refused;
              }
              catch (Exception ex)
              {
                  Logger.Error(ex, "Error setting band {BandType}", bandType);
                  return TuneResult.Refused;
              }
          }
      }

      /// <summary>
      /// Restarts streaming after a band change paused it. Puts the receiver in the
      /// <see cref="ReceiverState.Error"/> state when streaming cannot be restarted.
      /// </summary>
      /// <param name="wasStreaming">Whether streaming was running before the band change.</param>
      /// <returns>False only when streaming was running and could not be restarted.</returns>
      private bool ResumeAfterBandChange(bool wasStreaming)
      {
          if (!wasStreaming)
          {
              return true;
          }

          if (!_device.StartStreaming())
          {
              Logger.Error("Failed to restart streaming after band change");
              SetState(ReceiverState.Error);
              return false;
          }

          Logger.Debug("Streaming resumed after band change");
          return true;
      }

      /// <inheritdoc/>
      public bool SetModulation(ModulationType modulation)
      {
          try
          {
              _currentModulation = modulation;

              // Re-run full signal processing setup since modulation change
              // affects bandwidth, demod rate, and filter configuration.
              if (_state == ReceiverState.Running || _state == ReceiverState.Scanning)
              {
                  _pendingFmStart = new FmChainStart("modulation change", ByRecovery: false);
                  SetupSignalProcessing();

                  // AUD-101: the new modulation may start or stop using the AM chain, which
                  // moves the device centre (DeviceFrequency); tune it again.
                  if (_device.IsOpen)
                  {
                      long deviceHz = DeviceFrequency(_currentFrequencyHz);
                      bool tuned;
                      lock (_tuneLock)
                      {
                          tuned = _device.SetFrequency(deviceHz);
                      }
                      Interlocked.Increment(ref _tuneGeneration);
                      if (!tuned)
                      {
                          Logger.Warning("The device refused {Frequency} after the modulation changed to {Modulation}",
                              RadioBand.FormatFrequency(deviceHz), modulation);
                      }
                  }
              }
              else
              {
                  _demodulator = DemodulatorFactory.Create(modulation);
                  _demodulator.SampleRate = _demodSampleRate > 0
                      ? _demodSampleRate : DefaultSdrSampleRate;
                  _demodulator.Bandwidth = DemodulatorFactory.GetRecommendedBandwidth(modulation);
              }

              Logger.Debug("Modulation set to {Modulation}", modulation);
              return true;
          }
          catch (Exception ex)
          {
              Logger.Error(ex, "Error setting modulation {Modulation}", modulation);
              return false;
          }
      }

      /// <inheritdoc/>
      public ModulationType CurrentModulation => _currentModulation;

      /// <summary>
      /// Gets whether a stereo pilot tone (19 kHz) is currently detected.
      /// True only for WFM when the stereo decoder has locked onto the pilot.
      /// </summary>
      public bool StereoDetected => _stereoDecoder?.StereoDetected ?? false;

      /// <summary>
      /// Gets the RDS Program Service station name (up to 8 characters), or null if not decoded.
      /// Only available for WFM (FM broadcast) when the station transmits RDS.
      /// </summary>
      public string? RdsStationName => _rdsDecoder?.StationName;

      /// <summary>
      /// Gets the RDS Radio Text (up to 64 characters), or null if not decoded.
      /// Often contains "Artist - Title" or station slogan.
      /// </summary>
      public string? RdsRadioText => _rdsDecoder?.RadioText;

      /// <summary>
      /// Gets the RDS Program Type name (e.g., "Rock", "News"), or null if not decoded.
      /// </summary>
      public string? RdsProgramTypeName => _rdsDecoder?.ProgramTypeName;

      /// <summary>
      /// Gets the RDS PI (Program Identification) code — a 16-bit station
      /// identifier that does not rotate. Null until the first valid RDS
      /// frame has been decoded after lock. Task #80 v4 — the call sign
      /// decode (<c>RbdsCallSignDecoder</c>) consumes this.
      /// </summary>
      public ushort? RdsProgramId => _rdsDecoder?.ProgramId;

      /// <inheritdoc/>
      public IReadOnlyList<RadioBand> GetAvailableBands() => BandPresets.AllBands;

      /// <summary>
      /// Gets the current band
      /// </summary>
      public RadioBand CurrentBand => _currentBand;

      #endregion

      #region IRadioControl Audio Implementation

      /// <inheritdoc/>
      public float Volume
      {
          get => _volume;
          set
          {
              _volume = Math.Clamp(value, 0f, 1f);
              Logger.Debug("Volume set to {Volume:P0}", _volume);
          }
      }

      /// <inheritdoc/>
      public bool IsMuted
      {
          get => _isMuted;
          set
          {
              _isMuted = value;
              Logger.Debug("Mute set to {Muted}", value);
          }
      }

      /// <inheritdoc/>
      public float SquelchThreshold
      {
          get => _squelchThreshold;
          set
          {
              _squelchThreshold = Math.Clamp(value, 0f, 1f);
              Logger.Debug("Squelch threshold set to {Threshold:P0}", _squelchThreshold);
          }
      }

      /// <inheritdoc/>
      public void SetAudioOutputFormat(AudioFormat format)
      {
          _audioFormat = format;
          Logger.Debug("Audio format set to {Format}", format);
      }

      /// <inheritdoc/>
      public AudioFormat GetAudioOutputFormat() => _audioFormat;

      #endregion

      #region IRadioControl Gain Implementation

      /// <inheritdoc/>
      public bool AutoGainEnabled
      {
          get => _autoGain;
          set
          {
              _autoGain = value;

              // A user gain change cancels a live band sweep. The field is
              // assigned first so the sweep's cleanup re-applies this value.
              CancelSweep();

              if (_device.IsOpen)
              {
                  lock (_tuneLock)
                  {
                      _device.SetGainMode(value);
                      if (!value)
                      {
                          _device.SetGain(_manualGain);
                      }
                  }
              }
              Logger.Debug("Auto gain set to {Enabled}", value);
          }
      }

      /// <inheritdoc/>
      public float Gain
      {
          get => _device.IsOpen ? _device.GetGain() : _manualGain;
          set
          {
              _manualGain = value;

              // As for AutoGainEnabled: field first, then cancel the sweep.
              CancelSweep();

              if (_device.IsOpen && !_autoGain)
              {
                  lock (_tuneLock)
                  {
                      _device.SetGain(value);
                  }
              }
              Logger.Debug("Gain set to {Gain} dB", value);
          }
      }

      #endregion

      #region IRadioControl State Implementation

      /// <inheritdoc/>
      public RadioState GetRadioState()
      {
          return new RadioState
          {
              FrequencyHz = _currentFrequencyHz,
              CurrentBand = _currentBand.Type,
              Modulation = _currentModulation,
              State = _state,
              SignalStrength = _lastSignalStrength,
              Volume = _volume,
              SquelchOpen = !SquelchApplies || _lastSignalStrength >= _squelchThreshold,
              SquelchThreshold = _squelchThreshold,
              IsMuted = _isMuted,
              GainDb = _device.IsOpen ? _device.GetGain() : _manualGain,
              AutoGainEnabled = _autoGain,
              BandwidthHz = _currentBand.DefaultBandwidthHz,
              AudioFormat = _audioFormat,
              DeviceName = _device.DeviceInfo.Name
          };
      }

      /// <inheritdoc/>
      /// <remarks>
      /// The value the squelch, the seek scan's stop test and <see cref="SignalStrengthUpdated"/> use,
      /// updated once per IQ block. On the HF bands (AM and Shortwave,
      /// <see cref="HfBands.Requires(BandType)"/>; AUD-101) it is the level of the AM chain's
      /// filtered channel on a dB scale, <c>(L + 60) / 60</c> with L the mean in-channel power in
      /// dBFS, never below 0 (<see cref="AmReceiveChain.Strength"/>): 0 is −60 dBFS and each 0.01 is
      /// 0.6 dB. On every other band, Aircraft included, it is the linear wideband RMS magnitude of
      /// about 256 raw IQ samples spread over the block (<see cref="WidebandSignalStrength"/>).
      /// </remarks>
      public float SignalStrength => _lastSignalStrength;

      /// <summary>
      /// Whether the squelch is applied to the current band: false on an HF band (AM, Shortwave;
      /// <see cref="HfBands.Requires(BandType)"/>), true on every other band (AUD-99).
      /// </summary>
      /// <remarks>
      /// <para>
      /// The squelch compares <see cref="SignalStrength"/> with <see cref="SquelchThreshold"/>. In
      /// AUD-99 that was the RMS of the raw IQ capture, and in HF direct-sampling mode (the tuner in
      /// standby, no front-end gain) it read about 0.02 on the box, under the 0.1 default on every
      /// channel: the squelch closed for good and AM and SW were exact digital silence.
      /// </para>
      /// <para>
      /// Since AUD-101 AM and SW report the AM chain's in-channel level instead, and the squelch is
      /// still not applied to them: with no station receivable on the box there is no calibrated
      /// in-channel threshold yet, and the accepted behaviour on AM and SW is low-level noise (held
      /// down by the AGC's maximum gain) rather than silence. Mute, the sweep silencing and its tail
      /// still apply.
      /// </para>
      /// <para>
      /// Aircraft (tuner mode) keeps the squelch on main's wideband-RMS formula, although its audio
      /// goes through the AM chain. The samples differ from main's: Aircraft is offset-tuned, so
      /// the RMS covers f−180…f+60 kHz rather than f±120 kHz. On the in-channel scale the 0.1
      /// default would sit at −54 dBFS, and on the box (2026-10-12) empty Aircraft channels read
      /// −18 to −26 dBFS in the channel with the tuner's AGC holding the noise up. On the wideband
      /// scale 121.5 MHz read 0.25 and the channels a seek passed 0.22-0.25, also above 0.1: the squelch is open on empty
      /// channels either way (see archive/queue/AUD-101.md, box UAT).
      /// </para>
      /// </remarks>
      private bool SquelchApplies => !HfBands.Requires(Volatile.Read(ref _currentBand).Type);

      #endregion

      #region Live Band Sweep

      /// <summary>
      /// True while <see cref="SweepChannels"/> or <see cref="SweepPlan"/> is running.
      /// </summary>
      public bool IsSweeping => _isSweeping;

      /// <summary>
      /// Test seam: invoked on the sweep thread when a live sweep read finds
      /// no IQ block queued, and once more if still none is queued after
      /// that (the first block raised after a hop is dropped), before the
      /// read waits. Lets tests deliver blocks exactly when the sweep needs
      /// them instead of racing a clock.
      /// </summary>
      internal Action? SweepAwaitingSamples { get; set; }

      /// <summary>
      /// Test seam: invoked by <see cref="CancelSweep"/> right after it
      /// cancels a running sweep, on the cancelling thread.
      /// </summary>
      internal Action? SweepCancelRequested { get; set; }

      /// <summary>
      /// Time a live sweep read waits for the next IQ block before the sweep
      /// fails with <see cref="TimeoutException"/>.
      /// </summary>
      internal static TimeSpan SweepReadTimeout { get; } = TimeSpan.FromSeconds(2);

      /// <summary>
      /// Measures <paramref name="channels"/> using the running receiver's own
      /// stream, then returns to the station that was playing. Blocks the
      /// calling thread for the duration of the sweep.
      /// </summary>
      /// <remarks>
      /// <para>
      /// Hops are made with the device's <c>SetFrequency</c> directly, not
      /// through the receiver's tuning methods: <see cref="CurrentFrequency"/>
      /// does not change and <see cref="FrequencyChanged"/> is not raised by
      /// the sweep. Output is silenced for the duration through a flag that is
      /// separate from <see cref="IsMuted"/>. The device is put in manual gain
      /// at <paramref name="gainDb"/> so levels are comparable across sweeps.
      /// </para>
      /// <para>
      /// When the sweep ends — completed, cancelled or failed — the device is
      /// retuned to the value of <see cref="CurrentFrequency"/> at that moment
      /// (so a user tune made mid-sweep wins), the gain mode recorded in
      /// <see cref="AutoGainEnabled"/> / <see cref="Gain"/> is re-applied, the
      /// RDS decoder is reset, and then the silencing flag is cleared (the next
      /// two processed blocks are still silenced) before <see cref="IsSweeping"/>
      /// turns false.
      /// </para>
      /// <para>
      /// <see cref="SetFrequency"/>, <see cref="SetFrequencyInBand"/>, the
      /// tune up/down methods, <see cref="SetBand"/>, the
      /// <see cref="AutoGainEnabled"/> and <see cref="Gain"/> setters, a seek
      /// scan and <see cref="Shutdown"/> all call <see cref="CancelSweep"/>.
      /// </para>
      /// <para>
      /// After each hop, the first IQ block delivered by the device is
      /// dropped and blocks already queued are discarded, so every block the
      /// sweep reads for a channel came from a device read that began after
      /// the hop's <c>SetFrequency</c> returned. That holds for a device that
      /// reads and raises blocks one at a time on a single thread, as
      /// <c>RtlSdrDevice</c>'s streaming loop does. The sweep's settling read
      /// then discards one more block for samples the hardware captured
      /// before the retune took effect.
      /// </para>
      /// </remarks>
      /// <param name="channels">Channel centres to measure, in Hz.</param>
      /// <param name="gainDb">Manual tuner gain used for the sweep, in dB.</param>
      /// <param name="progress">Receives a report after each channel.</param>
      /// <param name="ct">Cancels the sweep.</param>
      /// <param name="samplesPerMeasurement">Samples per settling read and per measurement read.</param>
      /// <returns>The measured channel levels.</returns>
      /// <exception cref="ArgumentOutOfRangeException">
      /// <paramref name="samplesPerMeasurement"/> fails
      /// <see cref="BandSweeper.IsValidSamplesPerMeasurement"/>. Checked before
      /// anything else is touched.
      /// </exception>
      /// <exception cref="InvalidOperationException">
      /// The receiver is not in the <see cref="ReceiverState.Running"/> state, a
      /// seek scan is running, another sweep is running, or the band of the first channel
      /// needs the other HF mode (<see cref="LiveSweepModeConflict"/>; AUD-99).
      /// </exception>
      /// <exception cref="OperationCanceledException">The sweep was cancelled.</exception>
      /// <exception cref="TimeoutException">No IQ block arrived within <see cref="SweepReadTimeout"/>.</exception>
      public IReadOnlyList<ChannelLevel> SweepChannels(
          IReadOnlyList<long> channels,
          float gainDb,
          IProgress<BandSweepProgress>? progress,
          CancellationToken ct,
          int samplesPerMeasurement = BandSweeper.DefaultSamplesPerMeasurement)
      {
          ArgumentNullException.ThrowIfNull(channels);
          BandSweeper.ThrowIfInvalidSamplesPerMeasurement(samplesPerMeasurement);

          return RunLiveSweep(
              plan: null,
              needsHf: channels.Count > 0 && HfBands.RequiresFrequency(channels[0]),
              gainDb,
              ct,
              (tuner, token) => BandSweeper.Sweep(tuner, channels, samplesPerMeasurement, progress, token));
      }

      /// <summary>
      /// Why a live sweep of <paramref name="plan"/> cannot run on this receiver as it is tuned now,
      /// or null when the HF mode allows it (AUD-99). A live sweep never switches HF direct sampling
      /// under the playing radio, so a plan whose band needs the other mode
      /// (<see cref="HfBands.Requires(BandSweepPlan)"/>) is refused.
      /// </summary>
      /// <param name="plan">The plan a live sweep would measure.</param>
      public string? LiveSweepModeConflict(BandSweepPlan plan)
      {
          ArgumentNullException.ThrowIfNull(plan);
          return ModeConflict(HfBands.Requires(plan), plan.Band);
      }

      private string? ModeConflict(bool needsHf, string what)
      {
          bool hfMode = _device.IsHfMode;
          if (needsHf == hfMode)
          {
              return null;
          }

          return needsHf
              ? $"A live sweep of {what} needs HF direct sampling, but the radio is playing {_currentBand.Name} with the tuner; the mode is not switched under the playing radio."
              : $"A live sweep of {what} needs the tuner, but the radio is playing {_currentBand.Name} with HF direct sampling; the mode is not switched under the playing radio.";
      }

      /// <summary>
      /// Measures every channel of <paramref name="plan"/> using the running receiver's own
      /// stream, then returns to the station that was playing (AUD-91). Blocks the calling
      /// thread for the duration of the sweep.
      /// </summary>
      /// <remarks>
      /// Runs under exactly the rules of <see cref="SweepChannels"/> (the two share one body):
      /// the sweep-only silencing, manual gain, the retune to <see cref="CurrentFrequency"/> and
      /// the gain-mode restore when it ends, cancellation by a user tune, band, gain, seek-scan
      /// or shutdown, and the dropped block after each hop. Each hop is one tune of the plan;
      /// see <see cref="BandSweeper.Sweep(ISweepTuner, BandSweepPlan, int, IProgress{BandSweepProgress}?, CancellationToken)"/>.
      /// </remarks>
      /// <param name="plan">The band's sweep plan.</param>
      /// <param name="gainDb">Manual tuner gain used for the sweep, in dB.</param>
      /// <param name="progress">Receives a report after each tune.</param>
      /// <param name="ct">Cancels the sweep.</param>
      /// <param name="samplesPerMeasurement">Samples per settling read and per measurement read.</param>
      /// <returns>The measured channel levels.</returns>
      /// <exception cref="ArgumentOutOfRangeException">
      /// <paramref name="samplesPerMeasurement"/> fails
      /// <see cref="BandSweeper.IsValidSamplesPerMeasurement"/>. Checked before anything else is touched.
      /// </exception>
      /// <exception cref="InvalidOperationException">
      /// The receiver is not in the <see cref="ReceiverState.Running"/> state, a seek scan is
      /// running, another sweep is running, the receiver's IQ sample rate is lower than
      /// <see cref="BandSweepPlan.SampleRate"/>, or the plan's band needs the other HF mode
      /// (<see cref="LiveSweepModeConflict"/>; AUD-99). Checked before anything else is touched.
      /// </exception>
      /// <exception cref="OperationCanceledException">The sweep was cancelled.</exception>
      /// <exception cref="TimeoutException">No IQ block arrived within <see cref="SweepReadTimeout"/>.</exception>
      public IReadOnlyList<ChannelLevel> SweepPlan(
          BandSweepPlan plan,
          float gainDb,
          IProgress<BandSweepProgress>? progress,
          CancellationToken ct,
          int samplesPerMeasurement = BandSweeper.DefaultSamplesPerMeasurement)
      {
          ArgumentNullException.ThrowIfNull(plan);
          BandSweeper.ThrowIfInvalidSamplesPerMeasurement(samplesPerMeasurement);

          return RunLiveSweep(
              plan,
              needsHf: HfBands.Requires(plan),
              gainDb,
              ct,
              (tuner, token) => BandSweeper.Sweep(tuner, plan, samplesPerMeasurement, progress, token));
      }

      /// <summary>
      /// The body shared by <see cref="SweepChannels"/> and <see cref="SweepPlan"/>: registers
      /// the sweep, silences output, sets manual gain, installs the IQ tap, runs
      /// <paramref name="sweep"/>, and restores everything in the <c>finally</c>.
      /// </summary>
      /// <param name="plan">When not null, the receiver's sample rate is checked against it under the state lock.</param>
      /// <param name="needsHf">
      /// Whether the swept band needs HF direct sampling. Checked against the device's mode under the
      /// state lock: a mismatch throws <see cref="InvalidOperationException"/> (AUD-99).
      /// </param>
      /// <param name="gainDb">Manual tuner gain used for the sweep, in dB.</param>
      /// <param name="ct">Cancels the sweep.</param>
      /// <param name="sweep">Runs the sweep over the live tuner with the sweep's own token.</param>
      private IReadOnlyList<ChannelLevel> RunLiveSweep(
          BandSweepPlan? plan,
          bool needsHf,
          float gainDb,
          CancellationToken ct,
          Func<ISweepTuner, CancellationToken, IReadOnlyList<ChannelLevel>> sweep)
      {
          CancellationTokenSource sweepCts;
          lock (_sweepStateLock)
          {
              if (_state != ReceiverState.Running)
              {
                  throw new InvalidOperationException($"Receiver must be running to sweep (state {_state})");
              }
              if (_isScanning)
              {
                  throw new InvalidOperationException("Cannot sweep while a seek scan is running");
              }
              if (_isSweeping)
              {
                  throw new InvalidOperationException("A band sweep is already running");
              }
              if (plan != null)
              {
                  BandSweeper.ThrowIfSampleRateTooLow(_sdrSampleRate, plan);
              }
              if (ModeConflict(needsHf, plan?.Band ?? "these channels") is string conflict)
              {
                  throw new InvalidOperationException(conflict);
              }

              sweepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
              _sweepCts = sweepCts;
              _isSweeping = true;
              _sweepIdle.Reset();
          }

          _sweepMuted = true;
          LiveSweepTuner tuner = new(this, sweepCts.Token);
          try
          {
              lock (_tuneLock)
              {
                  _device.SetGainMode(false);
                  _device.SetGain(gainDb);
              }

              _sweepTap = tuner.Enqueue;
              return sweep(tuner, sweepCts.Token);
          }
          finally
          {
              _sweepTap = null;

              lock (_tuneLock)
              {
                  // Read the field now: a user tune made during the sweep
                  // assigned it before cancelling us, so it wins. The sweep tuned
                  // its own centres; the radio's tune goes through DeviceFrequency
                  // (AUD-101: the AM offset).
                  _device.SetFrequency(DeviceFrequency(_currentFrequencyHz));
                  _device.SetGainMode(_autoGain);
                  if (!_autoGain)
                  {
                      _device.SetGain(_manualGain);
                  }
              }

              // Blocks already queued for DSP were captured on swept
              // channels; drop them so they are neither played nor fed to RDS.
              DrainProcessingQueue();
              _rdsDecoder?.Reset();
              tuner.Complete();

              // Unmute before the sweep is published as finished, so a sweep
              // that starts the moment this one is seen idle cannot have its
              // _sweepMuted cleared by this cleanup. The tail count is set
              // before the flag drops so no block between the two plays.
              _sweepMuteTailBlocks = SweepMuteTailBlockCount;
              _sweepMuted = false;

              lock (_sweepStateLock)
              {
                  _sweepCts = null;
                  _isSweeping = false;
              }
              sweepCts.Dispose();
              _sweepIdle.Set();
          }
      }

      /// <summary>
      /// Requests cancellation of a running <see cref="SweepChannels"/> or <see cref="SweepPlan"/> call.
      /// Returns immediately; the sweep's own cleanup restores the station.
      /// Does nothing when no sweep is running.
      /// </summary>
      public void CancelSweep()
      {
          lock (_sweepStateLock)
          {
              if (_sweepCts != null && !_sweepCts.IsCancellationRequested)
              {
                  Logger.Debug("Cancelling live band sweep");
                  _sweepCts.Cancel();
                  SweepCancelRequested?.Invoke();
              }
          }
      }

      /// <summary>
      /// Returns true (and counts one down) while post-sweep tail blocks
      /// remain to be silenced. Called only from the DSP thread.
      /// </summary>
      private bool ConsumeSweepMuteTail()
      {
          int remaining = _sweepMuteTailBlocks;
          if (remaining <= 0)
          {
              return false;
          }
          _sweepMuteTailBlocks = remaining - 1;
          return true;
      }

      private void DrainProcessingQueue()
      {
          BlockingCollection<IqBlock>? queue = _processingQueue;
          if (queue == null)
          {
              return;
          }

          try
          {
              while (queue.TryTake(out _))
              {
              }
          }
          catch (ObjectDisposedException)
          {
              // Shutdown disposed the queue concurrently — nothing to drain.
          }
      }

      /// <summary>
      /// <see cref="ISweepTuner"/> over the running receiver: hops with the
      /// device directly and reads IQ blocks copied from the receiver's tap.
      /// </summary>
      private sealed class LiveSweepTuner : ISweepTuner
      {
          // Small bound: the sweep only needs the blocks after each retune.
          // When full, the oldest block is dropped.
          private const int QueueCapacity = 4;

          private readonly RadioReceiver _owner;
          private readonly CancellationToken _ct;
          private readonly BlockingCollection<IqSample[]> _queue =
              new(new ConcurrentQueue<IqSample[]>(), QueueCapacity);
          // Guards the post-hop drain + _dropNextBlock against Enqueue.
          private readonly object _hopLock = new();
          // Set by Tune after each hop; the next block Enqueue sees is
          // dropped. That block's device read may have started before the
          // hop (it was in flight, or captured and not yet raised, when
          // SetFrequency ran). The block after it was read only once the
          // dropped one had been raised, i.e. after the hop.
          private bool _dropNextBlock;

          public LiveSweepTuner(RadioReceiver owner, CancellationToken ct)
          {
              _owner = owner;
              _ct = ct;
          }

          public int SampleRate => _owner._sdrSampleRate;

          public bool Tune(long frequencyHz)
          {
              bool ok;
              lock (_owner._tuneLock)
              {
                  // Checked inside the lock so no hop is issued after a
                  // cancelling user tune has taken the lock.
                  _ct.ThrowIfCancellationRequested();
                  ok = _owner._device.SetFrequency(frequencyHz);
              }

              lock (_hopLock)
              {
                  // Blocks queued so far were raised before this point, so
                  // their reads began before the hop returned: drop them, and
                  // drop the next block raised as well (see _dropNextBlock).
                  while (_queue.TryTake(out _))
                  {
                  }
                  _dropNextBlock = true;
              }

              return ok;
          }

          public int Read(Span<IqSample> buffer, CancellationToken ct)
          {
              int filled = 0;
              while (filled < buffer.Length)
              {
                  if (!_queue.TryTake(out IqSample[]? block))
                  {
                      _owner.SweepAwaitingSamples?.Invoke();

                      // Ask the test seam a second time when its block was
                      // the one dropped after a hop (see _dropNextBlock).
                      // With no seam installed this is one more empty poll.
                      if (!_queue.TryTake(out block))
                      {
                          _owner.SweepAwaitingSamples?.Invoke();
                          if (!_queue.TryTake(out block, (int)SweepReadTimeout.TotalMilliseconds, ct))
                          {
                              throw new TimeoutException(
                                  $"No IQ samples from the receiver within {SweepReadTimeout.TotalSeconds}s during a band sweep");
                          }
                      }
                  }

                  int count = Math.Min(block.Length, buffer.Length - filled);
                  block.AsSpan(0, count).CopyTo(buffer.Slice(filled));
                  filled += count;
              }

              return filled;
          }

          /// <summary>Called from the USB read thread via the receiver's tap.</summary>
          public void Enqueue(IqSample[] samples)
          {
              // Copied because a device may reuse its block array between
              // reads (MockSdrDevice does).
              IqSample[] copy = (IqSample[])samples.Clone();
              lock (_hopLock)
              {
                  if (_dropNextBlock)
                  {
                      _dropNextBlock = false;
                      return;
                  }

                  try
                  {
                      while (!_queue.TryAdd(copy))
                      {
                          _queue.TryTake(out _);
                      }
                  }
                  catch (InvalidOperationException)
                  {
                      // Complete() already ran: the sweep has ended.
                  }
              }
          }

          /// <summary>Stops accepting blocks.</summary>
          public void Complete()
          {
              _queue.CompleteAdding();
          }
      }

      #endregion

      #region Private Methods

      private void SetupSignalProcessing()
      {
          var bandwidth = _currentBand.DefaultBandwidthHz;

          // Pipeline matches rtl_fm's proven approach:
          //   SDR (high rate) → IQ decimate → FM demod → audio decimate → output
          //
          // The IQ decimation step is critical: it band-limits the signal to
          // just the channel bandwidth BEFORE demodulation, preventing out-of-band
          // noise from corrupting the FM discriminator output.

          // Steps 1-2: Calculate paired SDR and demod rates that divide cleanly
          (_sdrSampleRate, _demodSampleRate) = CalculateRates(bandwidth);

          // AUD-101: AM modulation on a channel of up to 20 kHz (the AM, Shortwave and Aircraft
          // bands) has a chain of its own; see AmReceiveChain for the rates, the filters and why it
          // is separate. Every other modulation continues below, unchanged.
          if (AmReceiveChain.Applies(_currentModulation, bandwidth))
          {
              SetupAmSignalProcessing(bandwidth);
              return;
          }

          // AUD-107: narrowband FM (Weather, VHF) likewise; see NfmReceiveChain. Only WFM (and
          // any modulation on a channel too wide for both chains) continues below.
          if (NfmReceiveChain.Applies(_currentModulation, bandwidth))
          {
              SetupNfmSignalProcessing(bandwidth);
              return;
          }
          // The AM and NFM chains are cleared at the end of SetupGenericSignalProcessing, once the
          // generic fields are built, so a block the DSP thread takes meanwhile still runs the old chain's
          // DSP (its output may take the new _audioFormat: at most one block mistagged). If the
          // build below throws, the chains are cleared anyway (catch), so a stale AM offset cannot
          // outlive the band.
          try
          {
              SetupGenericSignalProcessing(bandwidth);
          }
          catch
          {
              _amChain = null;
              _nfmChain = null;
              throw;
          }
      }

      /// <summary>
      /// The generic path (WFM, and any modulation on a channel too wide for the AM and NFM chains):
      /// IQ decimation, demodulator, stereo/RDS for WFM, de-emphasis, audio decimation. Clears the AM
      /// and NFM chains last.
      /// </summary>
      private void SetupGenericSignalProcessing(int bandwidth)
      {
          // Step 3: IQ decimation (SDR rate → demod rate)
          var iqDecimFactor = _sdrSampleRate / _demodSampleRate;
          if (iqDecimFactor > 1)
          {
              // Cutoff at the channel half-bandwidth to select just the FM channel
              var channelCutoff = bandwidth / 2.0f;
              _iqDecimator = new Decimator(_sdrSampleRate, iqDecimFactor, channelCutoff);
          }
          else
          {
              _iqDecimator = null;
          }

          // Step 4: Demodulator operates at the intermediate rate
          _demodulator = DemodulatorFactory.Create(_currentModulation);
          _demodulator.SampleRate = _demodSampleRate;
          _demodulator.Bandwidth = bandwidth;

          // Step 5: FM stereo decoder (WFM only).
          // Must run BEFORE de-emphasis: the 19kHz pilot and 23-53kHz L-R subcarrier
          // would be attenuated by de-emphasis, making stereo decode impossible.

          // Detach the previous decoder's event (if any) before replacing it —
          // SetupSignalProcessing can be called multiple times for the same
          // receiver (modulation/band change), so leftover subscriptions
          // would leak and fire stale events from a discarded decoder.
          DetachRdsDecoder();

          if (_currentModulation == ModulationType.WFM)
          {
              _stereoDecoder = new StereoFmDecoder(_demodSampleRate);
              _rdsDecoder = new RdsDecoder(_demodSampleRate);
              _rdsDecoder.StationNameDecoded += OnRdsDecoderStationNameDecoded;
              _rdsDecoder.ProgramIdChanged += OnRdsDecoderProgramIdChanged;
              // AUD-106: a new WFM chain is an FM start for the acquisition watchdog.
              PublishFmStart();
          }
          else
          {
              _stereoDecoder = null;
              _rdsDecoder = null;
              _pendingFmStart = null;
          }

          // Step 6: De-emphasis filter (compensates FM pre-emphasis).
          // For stereo WFM, we need two independent filters (one per channel)
          // since de-emphasis is applied after stereo matrix decode.
          if (_currentModulation == ModulationType.WFM)
          {
              _deEmphasis = new DeEmphasisFilter(_demodSampleRate, 75f);
              _deEmphasisRight = new DeEmphasisFilter(_demodSampleRate, 75f);
          }
          else
          {
              _deEmphasis = null;
              _deEmphasisRight = null;
          }

          // Step 7: Audio decimation (demod rate → 48 kHz)
          // For stereo WFM, we need two independent decimators (one per channel).
          if (_demodSampleRate > DefaultAudioSampleRate)
          {
              _audioDecimator = new AudioDecimator(_demodSampleRate, DefaultAudioSampleRate);
              if (_stereoDecoder != null)
              {
                  _audioDecimatorRight = new AudioDecimator(_demodSampleRate, DefaultAudioSampleRate);
              }
              else
              {
                  _audioDecimatorRight = null;
              }
          }
          else
          {
              _audioDecimator = null;
              _audioDecimatorRight = null;
          }

          // WFM outputs interleaved stereo; all other modes output mono.
          _audioFormat = _currentModulation == ModulationType.WFM
              ? AudioFormat.Stereo
              : AudioFormat.Default;

          // Pre-allocate processing buffers based on expected USB chunk size.
          // RTL-SDR reads 32768 bytes = 16384 IQ pairs per USB transfer.
          const int maxIqChunk = 16384;
          var maxDemodSamples = _iqDecimator != null
              ? maxIqChunk / _iqDecimator.Factor + 1
              : maxIqChunk;
          _demodBuffer = new float[maxDemodSamples];
          _decimBuffer = new float[maxDemodSamples / (_audioDecimator?.Factor ?? 1) + 1];
          if (_iqDecimator != null)
          {
              _iqDecimBuffer = new IqSample[maxIqChunk / _iqDecimator.Factor + 1];
          }

          // Stereo buffers: interleaved L,R at demod rate, plus right-channel decimation
          if (_stereoDecoder != null)
          {
              _stereoDemodBuffer = new float[maxDemodSamples * 2]; // interleaved L,R
              _decimBufferRight = new float[maxDemodSamples / (_audioDecimator?.Factor ?? 1) + 1];
              _decimBufferRightOut = new float[maxDemodSamples / (_audioDecimator?.Factor ?? 1) + 1];
          }
          else
          {
              _stereoDemodBuffer = Array.Empty<float>();
              _decimBufferRight = Array.Empty<float>();
              _decimBufferRightOut = Array.Empty<float>();
          }

          // Pre-calculate expected audio output count for squelch silence delivery.
          // Each USB transfer is 16384 IQ pairs; after all decimation stages,
          // we get this many audio samples. For stereo, multiply by 2 for interleaved L,R.
          var iqDecimFactor2 = _iqDecimator?.Factor ?? 1;
          var audioDecimFactor = _audioDecimator?.Factor ?? 1;
          var monoOutputCount = maxIqChunk / iqDecimFactor2 / audioDecimFactor;
          _expectedAudioOutputCount = _stereoDecoder != null
              ? monoOutputCount * 2
              : monoOutputCount;
          _squelchSilenceBuffer = new float[_expectedAudioOutputCount];

          _amChain = null;
          _nfmChain = null;

          Logger.Information(
              "Signal processing: SDR={SdrRate} Hz → IQ decim {IqFactor}:1 → " +
              "Demod={DemodRate} Hz → {StereoMode} → Audio decim {AudioFactor}:1 → {AudioRate} Hz, " +
              "BW={Bandwidth} Hz, Modulation={Modulation}, Format={Format}",
              _sdrSampleRate, iqDecimFactor, _demodSampleRate,
              _stereoDecoder != null ? "Stereo decode" : "Mono",
              _demodSampleRate > DefaultAudioSampleRate
                  ? _demodSampleRate / DefaultAudioSampleRate : 1,
              DefaultAudioSampleRate, bandwidth, _currentModulation, _audioFormat);
      }

      /// <summary>
      /// Sets up the AM-modulation chain (AUD-101) at the SDR rate <see cref="CalculateRates"/> chose
      /// (240 kHz for every AM-modulated band): offset shift, channel filter and decimation to
      /// 48 kHz, envelope, audio low-pass, AGC. Mono at 48 kHz, so there is no audio decimator.
      /// </summary>
      private void SetupAmSignalProcessing(int bandwidth)
      {
          _pendingFmStart = null;   // AUD-106: the reason was for an FM chain; this is not one
          // RTL-SDR reads 32768 bytes = 16384 IQ pairs per USB transfer.
          const int maxIqChunk = 16384;
          var chain = new AmReceiveChain(_sdrSampleRate, bandwidth, maxIqChunk);

          _demodSampleRate = AmReceiveChain.DemodSampleRate;
          _iqDecimator = null;
          _demodulator = chain.Demodulator;

          DetachRdsDecoder();
          _stereoDecoder = null;
          _rdsDecoder = null;
          _deEmphasis = null;
          _deEmphasisRight = null;
          _audioDecimator = null;
          _audioDecimatorRight = null;
          _audioFormat = AudioFormat.Default;

          _expectedAudioOutputCount = maxIqChunk / chain.Channelizer.Decimation;
          _squelchSilenceBuffer = new float[_expectedAudioOutputCount];

          _amChain = chain;
          _nfmChain = null;

          Logger.Information(
              "Signal processing (AM): SDR={SdrRate} Hz → shift {Offset} Hz → channel filter {Cutoff} Hz ({ChannelTaps} taps), " +
              "decim {Decimation}:1 → Demod={DemodRate} Hz → audio low-pass {AudioCutoff} Hz ({AudioTaps} taps) → AGC → " +
              "{AudioRate} Hz mono, BW={Bandwidth} Hz, Modulation={Modulation}",
              _sdrSampleRate, chain.Channelizer.OffsetHz, chain.Channelizer.CutoffHz, chain.Channelizer.Taps,
              chain.Channelizer.Decimation, _demodSampleRate, chain.AudioFilter.CutoffFrequency, chain.AudioFilter.Taps,
              DefaultAudioSampleRate, bandwidth, _currentModulation);
      }

      /// <summary>
      /// Sets up the narrowband FM chain (AUD-107) at the SDR rate <see cref="CalculateRates"/> chose
      /// (240 kHz): channel filter and decimation to 48 kHz, then the discriminator. Mono at 48 kHz,
      /// so there is no audio decimator and no de-emphasis (the generic path had none for NFM either).
      /// The device is tuned on the frequency, as before (no offset).
      /// </summary>
      private void SetupNfmSignalProcessing(int bandwidth)
      {
          _pendingFmStart = null;   // AUD-106: the reason was for an FM chain; this is not one
          // RTL-SDR reads 32768 bytes = 16384 IQ pairs per USB transfer.
          const int maxIqChunk = 16384;
          var chain = new NfmReceiveChain(_sdrSampleRate, bandwidth, maxIqChunk);

          _demodSampleRate = NfmReceiveChain.DemodSampleRate;
          _iqDecimator = null;
          _demodulator = chain.Demodulator;

          DetachRdsDecoder();
          _stereoDecoder = null;
          _rdsDecoder = null;
          _deEmphasis = null;
          _deEmphasisRight = null;
          _audioDecimator = null;
          _audioDecimatorRight = null;
          _audioFormat = AudioFormat.Default;

          _expectedAudioOutputCount = maxIqChunk / chain.Channelizer.Decimation;
          _squelchSilenceBuffer = new float[_expectedAudioOutputCount];

          // Published before the AM chain is cleared: ProcessSamples prefers the AM chain, so a
          // block that sees both still runs one consistent chain.
          _nfmChain = chain;
          _amChain = null;

          Logger.Information(
              "Signal processing (NFM): SDR={SdrRate} Hz → channel filter {Cutoff} Hz ({ChannelTaps} taps), " +
              "decim {Decimation}:1 → Demod={DemodRate} Hz → {AudioRate} Hz mono, BW={Bandwidth} Hz, Modulation={Modulation}",
              _sdrSampleRate, chain.Channelizer.CutoffHz, chain.Channelizer.Taps, chain.Channelizer.Decimation,
              _demodSampleRate, DefaultAudioSampleRate, bandwidth, _currentModulation);
      }

      /// <summary>
      /// Detaches the current RDS decoder's events, if there is one. SetupSignalProcessing can be
      /// called many times for the same receiver, so a leftover subscription would fire stale
      /// events from a discarded decoder.
      /// </summary>
      private void DetachRdsDecoder()
      {
          if (_rdsDecoder != null)
          {
              _rdsDecoder.StationNameDecoded -= OnRdsDecoderStationNameDecoded;
              _rdsDecoder.ProgramIdChanged -= OnRdsDecoderProgramIdChanged;
          }
      }

      /// <summary>
      /// Calculates the SDR capture rate and demod rate together to ensure
      /// both divide cleanly (SDR → demod → audio are all integer ratios).
      /// Returns (sdrRate, demodRate).
      /// </summary>
      private static (int sdrRate, int demodRate) CalculateRates(int channelBandwidthHz)
      {
          // Strategy: set SDR rate as LOW as possible to minimize DSP work.
          // The RTL-SDR supports rates down to ~225 kHz. For WFM (200kHz BW),
          // a rate of 240kHz captures the full channel and divides cleanly
          // into 48kHz audio (5:1). No IQ decimation needed = no heavy FIR.
          //
          // For narrow modes (NFM 12.5kHz), we need ≥ 25kHz but the RTL-SDR
          // minimum is ~225kHz, so we pick 240kHz there too.

          // Find smallest audio-rate multiple ≥ channel bandwidth
          var minRate = Math.Max(channelBandwidthHz, DefaultAudioSampleRate * 2);
          var multiplier = (int)Math.Ceiling((double)minRate / DefaultAudioSampleRate);
          var demodRate = multiplier * DefaultAudioSampleRate;

          // SDR rate = demod rate (no IQ decimation), clamped to RTL-SDR min
          // Note: rates below ~225kHz are unstable on RTL-SDR hardware
          var sdrRate = Math.Max(demodRate, 225_000);

          // If sdrRate != demodRate, we need IQ decimation.
          // Try to keep them equal by rounding up demodRate.
          if (sdrRate > demodRate)
          {
              multiplier = (int)Math.Ceiling((double)sdrRate / DefaultAudioSampleRate);
              demodRate = multiplier * DefaultAudioSampleRate;
              sdrRate = demodRate;
          }

          return (sdrRate, demodRate);
      }

      /// <summary>
      /// Forwards the decoder's per-frame PS event up to receiver subscribers.
      /// Runs on the DSP processing thread; handlers should be cheap and
      /// non-blocking (e.g. push the sample into a stability tracker).
      /// </summary>
      private void OnRdsDecoderStationNameDecoded(object? sender, RdsStationNameDecodedEventArgs e)
      {
          RdsStationNameChanged?.Invoke(this, new RdsStationNameChangedEventArgs(e.Name));
      }

      /// <summary>
      /// Forwards the decoder's PI-changed event up to receiver subscribers.
      /// Task #80 v4 — fires once per tune (after first RDS frame decodes)
      /// and again only on actual PI mutation. Runs on the DSP processing
      /// thread; handlers should be cheap (e.g. decode the call sign and
      /// raise a state-changed flag).
      /// </summary>
      private void OnRdsDecoderProgramIdChanged(object? sender, ushort piCode)
      {
          ProgramIdChanged?.Invoke(this, piCode);
      }

      private void OnSamplesAvailable(object? sender, IqSamplesEventArgs e)
      {
          if (_state != ReceiverState.Running && _state != ReceiverState.Scanning)
    {
      return;
    }

    if (_processingCts?.IsCancellationRequested == true)
    {
      return;
    }

    // Live band sweep: hand the block to the sweep before the normal
    // enqueue. _sweepTap is null outside a sweep.
    _sweepTap?.Invoke(e.Samples);

    // Enqueue for async processing — returns immediately so the USB
    // read loop can issue the next rtlsdr_read_sync without waiting
    // for DSP processing to complete. If the queue is full (DSP can't
    // keep up), we drop the batch rather than blocking USB reads.
    try
          {
              if (!(_processingQueue?.TryAdd(new IqBlock(e.Samples, Volatile.Read(ref _tuneGeneration))) ?? false))
              {
                  Logger.Warning("DSP processing queue full — dropping IQ batch ({Samples} samples)",
                      e.Samples.Length);
              }
          }
          catch (ObjectDisposedException)
          {
              // Queue was disposed during shutdown race — producer and
              // disposer ran out of order. Drop the batch silently.
              // (Must precede InvalidOperationException — ODE derives from it.)
              Logger.Debug("DSP processing queue disposed while enqueuing — shutdown race");
          }
          catch (InvalidOperationException)
          {
              // Queue has been marked as complete (shutdown)
          }
      }

      private void ProcessingLoop()
      {
          Logger.Information("DSP processing thread started");
          try
          {
              foreach (var block in _processingQueue!.GetConsumingEnumerable(
                           _processingCts?.Token ?? CancellationToken.None))
              {
                  try
                  {
                      ProcessSamples(block.Samples, block.TuneGeneration);
                  }
                  catch (Exception ex)
                  {
                      Logger.Error(ex, "Error processing samples");
                  }
              }
          }
          catch (OperationCanceledException)
          {
              // Normal shutdown
          }
          catch (ObjectDisposedException)
          {
              // Shutdown race: Dispose() on another thread tore down
              // _processingQueue (or its underlying CTS) before this
              // consumer noticed CompleteAdding/Cancel. Treat as a
              // clean exit — letting this escape would unhandle on the
              // background thread and crash the host (e.g. test runner).
              Logger.Debug("DSP processing queue disposed during shutdown — exiting loop");
          }
          Logger.Information("DSP processing thread stopped");
      }

      /// <summary>
      /// The wideband signal strength: the RMS magnitude of about 256 raw IQ samples spread evenly
      /// over the block, on a linear scale (full-scale IQ reads 1). The formula <c>main</c> used for
      /// every band before AUD-101, and still used on every band except AM and Shortwave.
      /// </summary>
      /// <param name="samples">One IQ block; must not be empty.</param>
      private static float WidebandSignalStrength(IqSample[] samples)
      {
          // Calculate signal strength from a subset (avoid LINQ .Average allocation)
          var sumPower = 0f;
          var step = Math.Max(1, samples.Length / 256); // sample ~256 points
          var count = 0;
          for (var i = 0; i < samples.Length; i += step)
          {
              sumPower += samples[i].MagnitudeSquared;
              count++;
          }
          return MathF.Sqrt(sumPower / count);
      }

      private void ProcessSamples(IqSample[] samples, int tuneGeneration)
      {
          AmReceiveChain? am = _amChain;
          // One read of the band per block, so the level's scale and the squelch decision agree.
          bool hfBand = HfBands.Requires(Volatile.Read(ref _currentBand).Type);
          if (am != null)
          {
              // AUD-101: the chain counts blocks since the last tune (see
              // AmReceiveChain.BeginBlock) using the generation stamped when the block was raised,
              // and the channel filter runs on every block, squelched or not, so its history and
              // the in-channel level are always current.
              am.BeginBlock(tuneGeneration);
              am.Channelize(samples);
          }

          // AUD-101: the in-channel dB scale on the HF bands (AM, Shortwave) only. Aircraft keeps
          // the wideband RMS even though its audio goes through the AM chain: in tuner mode the
          // tuner's AGC holds the noise high, and on the box an empty Aircraft channel read
          // -18 to -26 dBFS in the channel, over the -30 dBFS that the 50 % seek stop means on the
          // dB scale and far over the -54 dBFS of the 0.1 squelch.
          _lastSignalStrength = am != null && hfBand ? am.Strength : WidebandSignalStrength(samples);

          SignalStrengthUpdated?.Invoke(this, new SignalStrengthEventArgs(_lastSignalStrength));

          // Check squelch — when closed or muted, deliver silence instead of nothing.
          // Delivering nothing starves the downstream BufferedSoundGenerator, causing
          // underruns that appear as zero-filled gaps in the audio output. By delivering
          // a silence buffer of the expected size, the buffer stays fed and the mixer
          // can cross-fade cleanly when the signal returns.
          // A live band sweep also silences output (_sweepMuted), and after it
          // ends the next few blocks that would otherwise play are silenced
          // too (ConsumeSweepMuteTail).
          // AUD-99: on an HF band the squelch is not applied (see SquelchApplies).
          bool squelchClosed = !hfBand && _lastSignalStrength < _squelchThreshold;
          if (squelchClosed || _isMuted || _sweepMuted || ConsumeSweepMuteTail())
          {
              // AUD-101: the AM chain's audio state (DC blocker, audio low-pass, AGC) does not
              // advance while no audio is produced, so it is re-seeded on the next block that is.
              am?.MarkSilenced();
              if (_expectedAudioOutputCount > 0 && AudioDataAvailable != null)
              {
                  // Silence buffer is pre-allocated and already zeroed
                  AudioDataAvailable.Invoke(this,
                      new AudioDataEventArgs(_squelchSilenceBuffer, _expectedAudioOutputCount, _audioFormat));
              }
              return;
          }

          if (am != null)
          {
              DeliverAudio(am.Audio, am.ProduceAudio());
              return;
          }

          // AUD-107: narrowband FM is filtered to its channel before the discriminator. Read once
          // per block, like the AM chain. The level above stays the wideband RMS on these bands.
          NfmReceiveChain? nfm = _nfmChain;
          if (nfm != null)
          {
              DeliverAudio(nfm.Audio, nfm.Process(samples));
              return;
          }

          // 1. IQ channel filter + decimation (SDR rate → demod rate)
          ReadOnlySpan<IqSample> iqForDemod;
          if (_iqDecimator != null)
          {
              var iqCount = _iqDecimator.Decimate(samples, _iqDecimBuffer);
              iqForDemod = _iqDecimBuffer.AsSpan(0, iqCount);
          }
          else
          {
              iqForDemod = samples;
          }

          // 2. FM demodulation (uses pre-allocated buffer)
          // Ensure buffer is large enough (handles unexpected chunk sizes)
          if (_demodBuffer.Length < iqForDemod.Length)
          {
              _demodBuffer = new float[iqForDemod.Length];
          }
          var demodCount = _demodulator.Demodulate(iqForDemod, _demodBuffer);

          int outputCount;
          float[] outputSamples;

          if (_stereoDecoder != null)
          {
              // === STEREO WFM PATH ===
              // 3a-rds. Capture PLL state BEFORE stereo decode processes this block.
              // RDS uses the same composite samples, so it needs the PLL phase at
              // the START of the block, not the end.
              var pllPhaseForRds = _stereoDecoder.PllPhase;
              var pllFreqForRds = _stereoDecoder.PllFrequency;

              // 3a. Stereo decode: composite MPX → interleaved L,R at demod rate.
              // This MUST happen before de-emphasis: the 19kHz pilot and 38kHz
              // subcarrier would be killed by the de-emphasis IIR.
              if (_stereoDemodBuffer.Length < demodCount * 2)
              {
                  _stereoDemodBuffer = new float[demodCount * 2];
              }
              _stereoDecoder.Decode(
                  _demodBuffer.AsSpan(0, demodCount),
                  _stereoDemodBuffer.AsSpan(0, demodCount * 2));

              // RDS decode runs on the same composite signal, using the PLL phase
              // captured before stereo decode advanced it.
              _rdsDecoder?.Process(
                  _demodBuffer.AsSpan(0, demodCount), demodCount,
                  pllPhaseForRds, pllFreqForRds);

              // AUD-106: judge the acquisition (pilot, RDS) while the composite is still intact.
              ObserveFmAcquisition(samples, tuneGeneration, _demodBuffer.AsSpan(0, demodCount));

              // 3b. De-emphasis per channel: de-interleave → process → re-interleave.
              // DeEmphasisFilter.Process works in-place on contiguous spans, so we
              // process even-indexed (L) and odd-indexed (R) samples using temp views.
              // To avoid allocation, work in-place by extracting channels, filtering,
              // and interleaving back.
              var leftSpan = _demodBuffer.AsSpan(0, demodCount);   // reuse demod buffer for left
              if (_decimBufferRight.Length < demodCount)
              {
                  _decimBufferRight = new float[demodCount];
              }
              var rightSpan = _decimBufferRight.AsSpan(0, demodCount);

              // De-interleave
              for (var i = 0; i < demodCount; i++)
              {
                  leftSpan[i] = _stereoDemodBuffer[i * 2];
                  rightSpan[i] = _stereoDemodBuffer[i * 2 + 1];
              }

              // Apply de-emphasis per channel
              _deEmphasis?.Process(leftSpan);
              _deEmphasisRight?.Process(rightSpan);

              // 3c. Audio decimation per channel (demod rate → 48 kHz)
              if (_audioDecimator != null && _audioDecimatorRight != null)
              {
                  var maxDecimOut = demodCount / _audioDecimator.Factor + 1;
                  if (_decimBuffer.Length < maxDecimOut)
                  {
                      _decimBuffer = new float[maxDecimOut];
                  }

                  var leftDecimCount = _audioDecimator.Decimate(leftSpan, _decimBuffer);

                  // Reuse pre-allocated right-channel decimation output buffer (avoid per-batch allocation).
                  // Allocating a new float[] here per IQ batch (~200 Hz) previously produced ~131 GB of
                  // garbage over a 2-day uptime test, causing GC storms (14M Gen0 in a single callback
                  // interval) that starved MiniAudio's audio callback and caused PipeWire to drop the
                  // stream. The radio reported "Playing" but produced no audio.
                  if (_decimBufferRightOut.Length < maxDecimOut)
                  {
                      _decimBufferRightOut = new float[maxDecimOut];
                  }
                  var rightDecimCount = _audioDecimatorRight.Decimate(rightSpan, _decimBufferRightOut);

                  // 3d. Interleave decimated L,R into stereo output
                  var stereoCount = Math.Min(leftDecimCount, rightDecimCount);
                  if (_stereoDemodBuffer.Length < stereoCount * 2)
                  {
                      _stereoDemodBuffer = new float[stereoCount * 2];
                  }
                  for (var i = 0; i < stereoCount; i++)
                  {
                      _stereoDemodBuffer[i * 2] = _decimBuffer[i];
                      _stereoDemodBuffer[i * 2 + 1] = _decimBufferRightOut[i];
                  }
                  outputCount = stereoCount * 2; // interleaved sample count
                  outputSamples = _stereoDemodBuffer;
              }
              else
              {
                  // No decimation needed — re-interleave directly
                  for (var i = 0; i < demodCount; i++)
                  {
                      _stereoDemodBuffer[i * 2] = leftSpan[i];
                      _stereoDemodBuffer[i * 2 + 1] = rightSpan[i];
                  }
                  outputCount = demodCount * 2;
                  outputSamples = _stereoDemodBuffer;
              }
          }
          else
          {
              // === MONO PATH (AM, NFM, etc.) ===
              // 3. De-emphasis (single-pole IIR — trivially cheap)
              if (_deEmphasis != null)
              {
                  _deEmphasis.Process(_demodBuffer.AsSpan(0, demodCount));
              }

              // 4. Audio decimation (demod rate → 48 kHz, uses pre-allocated buffer)
              if (_audioDecimator != null)
              {
                  if (_decimBuffer.Length < demodCount / _audioDecimator.Factor + 1)
                  {
                      _decimBuffer = new float[demodCount / _audioDecimator.Factor + 1];
                  }
                  outputCount = _audioDecimator.Decimate(
                      _demodBuffer.AsSpan(0, demodCount), _decimBuffer);
                  outputSamples = _decimBuffer;
              }
              else
              {
                  outputCount = demodCount;
                  outputSamples = _demodBuffer;
              }
          }

          DeliverAudio(outputSamples, outputCount);
      }

      /// <summary>Applies the volume and the clamp to the audio and raises <see cref="AudioDataAvailable"/>.</summary>
      private void DeliverAudio(float[] outputSamples, int outputCount)
      {
          // 5. Apply volume and clamp
          for (var i = 0; i < outputCount; i++)
          {
              var s = outputSamples[i] * _volume;
              outputSamples[i] = s > 1.0f ? 1.0f : s < -1.0f ? -1.0f : s;
          }

          // Pass the pre-allocated buffer directly — listeners must consume data
          // synchronously during the event callback (buffer is reused next call).
          AudioDataAvailable?.Invoke(this,
              new AudioDataEventArgs(outputSamples, outputCount, _audioFormat));
      }

      private void SetState(ReceiverState newState)
      {
          var oldState = _state;
          _state = newState;

          if (oldState != newState)
          {
              StateChanged?.Invoke(this, new ReceiverStateChangedEventArgs(oldState, newState));
          }
      }

      #endregion

      /// <inheritdoc/>
      public void Dispose()
      {
          Shutdown();

          // Detach decoder event so a stale subscription doesn't survive
          // a Shutdown→Startup cycle if the decoder is recreated.
          DetachRdsDecoder();

          _device.Dispose();
          GC.SuppressFinalize(this);
      }
  }

  /// <summary>
  /// Event arguments for audio data.
  /// IMPORTANT: The Samples buffer is reused between callbacks.
  /// Listeners must consume data synchronously during the event — do not stash the reference.
  /// </summary>
  public class AudioDataEventArgs : EventArgs
  {
      /// <summary>
      /// Gets the audio samples buffer. Only the first <see cref="SampleCount"/> elements are valid.
      /// </summary>
      public float[] Samples { get; }

      /// <summary>
      /// Gets the number of valid samples in the buffer.
      /// </summary>
      public int SampleCount { get; }

      /// <summary>
      /// Gets the audio format
      /// </summary>
      public AudioFormat Format { get; }

      /// <summary>
      /// Creates new audio data event args
      /// </summary>
      /// <param name="samples">Audio samples buffer (may be larger than valid data)</param>
      /// <param name="sampleCount">Number of valid samples</param>
      /// <param name="format">Audio format</param>
      public AudioDataEventArgs(float[] samples, int sampleCount, AudioFormat format)
      {
          Samples = samples;
          SampleCount = sampleCount;
          Format = format;
      }
  }

  /// <summary>
  /// Event arguments for <see cref="RadioReceiver.SeekStationFound"/> (AUD-100).
  /// </summary>
  public class SeekStationFoundEventArgs : EventArgs
  {
      /// <summary>Gets the frequency the seek stopped on, in Hz.</summary>
      public long FrequencyHz { get; }

      /// <summary>Gets the seek's signal reading there: the same value as <see cref="RadioReceiver.SignalStrength"/>.</summary>
      public float Strength { get; }

      /// <summary>Creates new seek-station event args.</summary>
      /// <param name="frequencyHz">Frequency, in Hz.</param>
      /// <param name="strength">Signal strength.</param>
      public SeekStationFoundEventArgs(long frequencyHz, float strength)
      {
          FrequencyHz = frequencyHz;
          Strength = strength;
      }
  }

  /// <summary>
  /// Event arguments for signal strength updates
  /// </summary>
  public class SignalStrengthEventArgs : EventArgs
  {
      /// <summary>
      /// Gets the signal strength (0.0 to 1.0)
      /// </summary>
      public float Strength { get; }

      /// <summary>
      /// Creates new signal strength event args
      /// </summary>
      /// <param name="strength">Signal strength</param>
      public SignalStrengthEventArgs(float strength)
      {
          Strength = strength;
      }
  }

  /// <summary>
  /// Event arguments for receiver state changes
  /// </summary>
  public class ReceiverStateChangedEventArgs : EventArgs
  {
      /// <summary>
      /// Gets the previous state
      /// </summary>
      public ReceiverState OldState { get; }

      /// <summary>
      /// Gets the new state
      /// </summary>
      public ReceiverState NewState { get; }

      /// <summary>
      /// Creates new state changed event args
      /// </summary>
      /// <param name="oldState">Previous state</param>
      /// <param name="newState">New state</param>
      public ReceiverStateChangedEventArgs(ReceiverState oldState, ReceiverState newState)
      {
          OldState = oldState;
          NewState = newState;
      }
  }

  /// <summary>
  /// Event arguments for <see cref="RadioReceiver.RdsStationNameChanged"/>.
  /// Carries the raw, per-frame Program Service name candidate produced by
  /// the RDS decoder. Fired on every successful PS group decode (~10 Hz when
  /// the signal is clean) — not just when the value changes — so downstream
  /// stability filters can apply their consensus logic at the underlying
  /// frame rate rather than at the property-poll rate.
  /// </summary>
  public class RdsStationNameChangedEventArgs : EventArgs
  {
      /// <summary>
      /// The candidate Program Service name decoded from this RDS frame.
      /// Trimmed, non-empty. Has NOT been filtered through any stability
      /// or confirmation logic.
      /// </summary>
      public string NewName { get; }

      /// <summary>
      /// Creates new RDS station-name changed event args.
      /// </summary>
      /// <param name="newName">The candidate PS name (trimmed, non-empty).</param>
      public RdsStationNameChangedEventArgs(string newName)
      {
          NewName = newName ?? throw new ArgumentNullException(nameof(newName));
      }
  }
