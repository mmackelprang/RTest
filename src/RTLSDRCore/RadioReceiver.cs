using RTLSDRCore.Bands;
using RTLSDRCore.DSP;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using Serilog;

namespace RTLSDRCore
{
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
        private AudioDecimator? _audioDecimator;
        private int _sdrSampleRate;
        private int _demodSampleRate;

        // Pre-allocated processing buffers to avoid GC pressure.
        // Sized in SetupSignalProcessing based on the expected USB chunk size.
        private float[] _demodBuffer = Array.Empty<float>();
        private float[] _decimBuffer = Array.Empty<float>();
        private IqSample[] _iqDecimBuffer = Array.Empty<IqSample>();

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

                    // Start processing loop
                    _processingCts = new CancellationTokenSource();
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
                    return;

                // Cancel any ongoing scan
                CancelScan();

                SetState(ReceiverState.Stopping);
                Logger.Information("Shutting down receiver");

                _device.SamplesAvailable -= OnSamplesAvailable;
                _processingCts?.Cancel();
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
                // Stop streaming before tuning to prevent USB pipe stalls,
                // unless already stopped (e.g., by SetBand which stops before calling this)
                var wasStreaming = _device.IsStreaming;
                if (wasStreaming)
                {
                    _device.StopStreaming();
                }

                var success = _device.SetFrequency(frequencyHz);

                if (wasStreaming)
                {
                    if (!_device.StartStreaming())
                    {
                        Logger.Error("Failed to restart streaming after frequency change");
                    }
                }

                if (!success)
                {
                    Logger.Warning("SetFrequency to {Frequency} returned false", RadioBand.FormatFrequency(frequencyHz));
                }
            }

            if (oldFrequency != frequencyHz)
            {
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
        public bool ScanFrequencyUp(long stepHz = 100_000, float signalThreshold = 0.3f, int dwellTimeMs = 100)
        {
            if (_state != ReceiverState.Running && _state != ReceiverState.Scanning)
            {
                throw new InvalidOperationException("Receiver must be started before scanning");
            }

            Logger.Information("Starting scan up from {Frequency}", RadioBand.FormatFrequency(_currentFrequencyHz));

            _scanCts = new CancellationTokenSource();
            _isScanning = true;
            SetState(ReceiverState.Scanning);

            try
            {
                while (_currentFrequencyHz + stepHz <= _currentBand.MaxFrequencyHz)
                {
                    if (_scanCts.Token.IsCancellationRequested)
                    {
                        Logger.Information("Scan cancelled");
                        SetState(ReceiverState.Running);
                        _isScanning = false;
                        return false;
                    }

                    SetFrequencyInternal(_currentFrequencyHz + stepHz);
                    Thread.Sleep(dwellTimeMs);

                    if (_lastSignalStrength >= signalThreshold)
                    {
                        Logger.Information("Signal found at {Frequency} (strength: {Strength:P0})",
                            RadioBand.FormatFrequency(_currentFrequencyHz), _lastSignalStrength);
                        SetState(ReceiverState.Running);
                        _isScanning = false;
                        return true;
                    }
                }

                Logger.Information("Scan reached upper band limit");
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
        public bool ScanFrequencyDown(long stepHz = 100_000, float signalThreshold = 0.3f, int dwellTimeMs = 100)
        {
            if (_state != ReceiverState.Running && _state != ReceiverState.Scanning)
            {
                throw new InvalidOperationException("Receiver must be started before scanning");
            }

            Logger.Information("Starting scan down from {Frequency}", RadioBand.FormatFrequency(_currentFrequencyHz));

            _scanCts = new CancellationTokenSource();
            _isScanning = true;
            SetState(ReceiverState.Scanning);

            try
            {
                while (_currentFrequencyHz - stepHz >= _currentBand.MinFrequencyHz)
                {
                    if (_scanCts.Token.IsCancellationRequested)
                    {
                        Logger.Information("Scan cancelled");
                        SetState(ReceiverState.Running);
                        _isScanning = false;
                        return false;
                    }

                    SetFrequencyInternal(_currentFrequencyHz - stepHz);
                    Thread.Sleep(dwellTimeMs);

                    if (_lastSignalStrength >= signalThreshold)
                    {
                        Logger.Information("Signal found at {Frequency} (strength: {Strength:P0})",
                            RadioBand.FormatFrequency(_currentFrequencyHz), _lastSignalStrength);
                        SetState(ReceiverState.Running);
                        _isScanning = false;
                        return true;
                    }
                }

                Logger.Information("Scan reached lower band limit");
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

            // Step 5: De-emphasis filter (compensates FM pre-emphasis).
            // This single-pole IIR is nearly free (1 multiply per sample)
            // and also attenuates the 19kHz pilot tone and stereo subcarrier.
            if (_currentModulation == ModulationType.WFM)
            {
                _deEmphasis = new DeEmphasisFilter(_demodSampleRate, 75f);
            }
            else
            {
                _deEmphasis = null;
            }

            // Step 6: Audio decimation (demod rate → 48 kHz)
            // The AudioDecimator's built-in anti-alias filter handles
            // rejection of content above 24kHz (Nyquist), so no separate
            // audio LPF is needed.
            if (_demodSampleRate > DefaultAudioSampleRate)
            {
                _audioDecimator = new AudioDecimator(_demodSampleRate, DefaultAudioSampleRate);
            }
            else
            {
                _audioDecimator = null;
            }

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

            _agc.Reset();

            Logger.Information(
                "Signal processing: SDR={SdrRate} Hz → IQ decim {IqFactor}:1 → " +
                "Demod={DemodRate} Hz → Audio decim {AudioFactor}:1 → {AudioRate} Hz, " +
                "BW={Bandwidth} Hz, Modulation={Modulation}",
                _sdrSampleRate, iqDecimFactor, _demodSampleRate,
                _demodSampleRate > DefaultAudioSampleRate
                    ? _demodSampleRate / DefaultAudioSampleRate : 1,
                DefaultAudioSampleRate, bandwidth, _currentModulation);
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
                return;

            if (_processingCts?.IsCancellationRequested == true)
                return;

            try
            {
                ProcessSamples(e.Samples);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error processing samples");
            }
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

            // Check squelch
            if (_lastSignalStrength < _squelchThreshold || _isMuted)
            {
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

            // 3. De-emphasis (single-pole IIR — trivially cheap)
            if (_deEmphasis != null)
            {
                _deEmphasis.Process(_demodBuffer.AsSpan(0, demodCount));
            }

            // 4. Audio decimation (demod rate → 48 kHz, uses pre-allocated buffer)
            int outputCount;
            float[] outputSamples;
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
}
