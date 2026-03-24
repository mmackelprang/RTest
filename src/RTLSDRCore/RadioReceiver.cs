using System.Collections.Concurrent;
using RTLSDRCore.Bands;
using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using Serilog;

namespace RTLSDRCore;

  /// <summary>
  /// Main radio receiver class that coordinates hardware, signal processing, and audio output
  /// </summary>
  public class RadioReceiver : IRadioControl, IDisposable
  {
      private static readonly ILogger Logger = Log.ForContext<RadioReceiver>();

      private readonly ISdrDevice _device;
      private readonly object _lock = new();

      private RadioBand _currentBand;
      private long _currentFrequencyHz;
      private ModulationType _currentModulation;
      private ReceiverState _state = ReceiverState.Stopped;

      private IDemodulator _demodulator;
      private AgcProcessor _agc;
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
      private float[] _decimBufferRight = Array.Empty<float>(); // right channel decimation
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
      private BlockingCollection<IqSample[]>? _processingQueue;
      private Thread? _processingThread;

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
      /// Creates a new radio receiver with the specified device
      /// </summary>
      /// <param name="device">SDR device to use</param>
      public RadioReceiver(ISdrDevice device)
      {
          _device = device ?? throw new ArgumentNullException(nameof(device));
          _currentBand = BandPresets.FmBroadcast;
          _currentFrequencyHz = _currentBand.CenterFrequencyHz;
          _currentModulation = _currentBand.DefaultModulation;
          _demodulator = DemodulatorFactory.Create(_currentModulation);
          _agc = new AgcProcessor();

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
      public static RadioReceiver? CreateWithFirstAvailableDevice()
      {
          var device = SdrDeviceFactory.CreateFirstAvailable();
          return device != null ? new RadioReceiver(device) : null;
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

                  // Setup signal processing chain first (calculates the
                  // appropriate SDR sample rate for the current modulation)
                  SetupSignalProcessing();

                  // Configure device with the modulation-appropriate sample rate
                  _device.SetSampleRate(_sdrSampleRate);
                  _device.SetFrequency(_currentFrequencyHz);
                  _device.SetGainMode(_autoGain);
                  if (!_autoGain)
                  {
                      _device.SetGain(_manualGain);
                  }

                  // Start streaming
                  if (!_device.StartStreaming())
                  {
                      Logger.Error("Failed to start streaming");
                      SetState(ReceiverState.Error);
                      return false;
                  }

                  // Start async processing pipeline: USB read thread enqueues
                  // IQ batches, dedicated processing thread runs DSP chain.
                  _processingCts = new CancellationTokenSource();
                  _processingQueue = new BlockingCollection<IqSample[]>(
                      new ConcurrentQueue<IqSample[]>(), boundedCapacity: 8);
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
                  SetState(ReceiverState.Error);
                  return false;
              }
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

              SetState(ReceiverState.Stopping);
              Logger.Information("Shutting down receiver");

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

              _device.StopStreaming();
              _device.Close();

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
      public bool SetFrequency(long frequencyHz)
      {
          // Try to find an appropriate band for this frequency
          var band = BandPresets.FindBandForFrequency(frequencyHz);

          if (band != null)
          {
              // Switch to the appropriate band
              return SetBand(band.Type, frequencyHz);
          }

          // No matching band found - check if within device range
          if (frequencyHz < _device.DeviceInfo.MinFrequencyHz ||
              frequencyHz > _device.DeviceInfo.MaxFrequencyHz)
          {
              Logger.Warning("Frequency {Frequency} is outside device range",
                  RadioBand.FormatFrequency(frequencyHz));
              return false;
          }

          // Create a custom band centered on this frequency
          var customBand = BandPresets.CreateCustomBand(
              "Custom",
              frequencyHz - 1_000_000,
              frequencyHz + 1_000_000,
              ModulationType.NFM);

          lock (_lock)
          {
              _currentBand = customBand;
              return SetFrequencyInternal(frequencyHz);
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

      private bool SetFrequencyInternal(long frequencyHz)
      {
          var oldFrequency = _currentFrequencyHz;
          _currentFrequencyHz = frequencyHz;

          if (_device.IsOpen)
          {
              // rtlsdr_set_center_freq is safe to call while streaming —
              // no need to stop/restart the USB pipe for frequency changes.
              var success = _device.SetFrequency(frequencyHz);

              if (!success)
              {
                  Logger.Warning("SetFrequency to {Frequency} returned false", RadioBand.FormatFrequency(frequencyHz));
              }
          }

          if (oldFrequency != frequencyHz)
          {
              _rdsDecoder?.Reset();
              FrequencyChanged?.Invoke(this, new FrequencyChangedEventArgs(oldFrequency, frequencyHz));
          }

          Logger.Debug("Frequency set to {Frequency}", RadioBand.FormatFrequency(frequencyHz));
          return true;
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
                      nextFreq = _currentBand.MinFrequencyHz;
                      Logger.Debug("Scan wrapped from upper to lower band edge");
                  }
                  else if (!ascending && nextFreq < _currentBand.MinFrequencyHz)
                  {
                      nextFreq = _currentBand.MaxFrequencyHz;
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

        if (_lastSignalStrength >= signalThreshold)
                  {
                      Logger.Information("Signal found at {Frequency} (strength: {Strength:F3}, threshold: {Threshold:F2}), pausing 2s",
                          RadioBand.FormatFrequency(_currentFrequencyHz), _lastSignalStrength, signalThreshold);

                      // Pause on signal for 2s so user can listen.
                      // WaitHandle.WaitOne returns true if signaled (cancelled),
                      // false on timeout — so scan resumes after the pause.
                      if (_scanCts.Token.WaitHandle.WaitOne(2000))
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
      public bool SetBand(BandType bandType, long? specificFrequency = null)
      {
          lock (_lock)
          {
              try
              {
                  var band = BandPresets.GetBand(bandType);
                  var wasStreaming = _device.IsStreaming;

                  Logger.Information("Switching to {Band}{Pause}", band.Name,
                      wasStreaming ? " (pausing stream)" : "");

                  // Stop streaming before tuning to avoid USB pipe stalls
                  if (wasStreaming)
                  {
                      _device.StopStreaming();
                  }

                  _currentBand = band;
                  _currentModulation = band.DefaultModulation;

                  var newFrequency = specificFrequency ?? _currentFrequencyHz;
                  newFrequency = band.ClampFrequency(newFrequency);

                  if (_state == ReceiverState.Running || _state == ReceiverState.Scanning)
                  {
                      SetupSignalProcessing();
                      _device.SetSampleRate(_sdrSampleRate);
                  }

                  SetFrequencyInternal(newFrequency);

                  // Restart streaming
                  if (wasStreaming)
                  {
                      if (!_device.StartStreaming())
                      {
                          Logger.Error("Failed to restart streaming after band change");
                          SetState(ReceiverState.Error);
                          return false;
                      }
                      Logger.Debug("Streaming resumed after band change");
                  }

                  return true;
              }
              catch (Exception ex)
              {
                  Logger.Error(ex, "Error setting band {BandType}", bandType);
                  return false;
              }
          }
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
                  SetupSignalProcessing();
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
              if (_device.IsOpen)
              {
                  _device.SetGainMode(value);
                  if (!value)
                  {
                      _device.SetGain(_manualGain);
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
              if (_device.IsOpen && !_autoGain)
              {
                  _device.SetGain(value);
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
              SquelchOpen = _lastSignalStrength >= _squelchThreshold,
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
      public float SignalStrength => _lastSignalStrength;

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
          if (_currentModulation == ModulationType.WFM)
          {
              _stereoDecoder = new StereoFmDecoder(_demodSampleRate);
              _rdsDecoder = new RdsDecoder(_demodSampleRate);
          }
          else
          {
              _stereoDecoder = null;
              _rdsDecoder = null;
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
          }
          else
          {
              _stereoDemodBuffer = Array.Empty<float>();
              _decimBufferRight = Array.Empty<float>();
          }

          _agc.Reset();

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

    // Enqueue for async processing — returns immediately so the USB
    // read loop can issue the next rtlsdr_read_sync without waiting
    // for DSP processing to complete. If the queue is full (DSP can't
    // keep up), we drop the batch rather than blocking USB reads.
    try
          {
              if (!(_processingQueue?.TryAdd(e.Samples) ?? false))
              {
                  Logger.Warning("DSP processing queue full — dropping IQ batch ({Samples} samples)",
                      e.Samples.Length);
              }
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
              foreach (var samples in _processingQueue!.GetConsumingEnumerable(
                           _processingCts?.Token ?? CancellationToken.None))
              {
                  try
                  {
                      ProcessSamples(samples);
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
          Logger.Information("DSP processing thread stopped");
      }

      private void ProcessSamples(IqSample[] samples)
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
          _lastSignalStrength = MathF.Sqrt(sumPower / count);

          SignalStrengthUpdated?.Invoke(this, new SignalStrengthEventArgs(_lastSignalStrength));

          // Check squelch — when closed or muted, deliver silence instead of nothing.
          // Delivering nothing starves the downstream BufferedSoundGenerator, causing
          // underruns that appear as zero-filled gaps in the audio output. By delivering
          // a silence buffer of the expected size, the buffer stays fed and the mixer
          // can cross-fade cleanly when the signal returns.
          if (_lastSignalStrength < _squelchThreshold || _isMuted)
          {
              if (_expectedAudioOutputCount > 0 && AudioDataAvailable != null)
              {
                  // Silence buffer is pre-allocated and already zeroed
                  AudioDataAvailable.Invoke(this,
                      new AudioDataEventArgs(_squelchSilenceBuffer, _expectedAudioOutputCount, _audioFormat));
              }
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

                  // Reuse pre-allocated right decimation buffer (avoid per-batch allocation)
                  if (_decimBufferRight.Length < maxDecimOut)
                  {
                      _decimBufferRight = new float[maxDecimOut];
                  }
                  var rightDecimCount = _audioDecimatorRight.Decimate(rightSpan, _decimBufferRight);

                  // 3d. Interleave decimated L,R into stereo output
                  var stereoCount = Math.Min(leftDecimCount, rightDecimCount);
                  if (_stereoDemodBuffer.Length < stereoCount * 2)
                  {
                      _stereoDemodBuffer = new float[stereoCount * 2];
                  }
                  for (var i = 0; i < stereoCount; i++)
                  {
                      _stereoDemodBuffer[i * 2] = _decimBuffer[i];
                      _stereoDemodBuffer[i * 2 + 1] = _decimBufferRight[i];
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
