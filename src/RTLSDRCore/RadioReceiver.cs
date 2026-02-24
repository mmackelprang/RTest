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
        private AudioDecimator? _audioDecimator;
        private LowPassFilter? _audioFilter;
        private int _sdrSampleRate;

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
                _demodulator = DemodulatorFactory.Create(modulation);
                _demodulator.SampleRate = DefaultSdrSampleRate;
                _demodulator.Bandwidth = DemodulatorFactory.GetRecommendedBandwidth(modulation);

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

            // Choose SDR sample rate appropriate for the modulation type.
            // Lower rates let the hardware RF filter handle channel selection,
            // avoiding the need for (and problems with) software IQ filtering.
            // The rate must be:
            //  - ≥ 2× channel bandwidth (Nyquist)
            //  - A multiple of the audio output rate (for clean decimation)
            //  - Within the RTL-SDR's practical range (~225 kHz – 3.2 MHz)
            _sdrSampleRate = CalculateSdrSampleRate(bandwidth);

            Logger.Information(
                "Signal processing: SDR={SdrRate} Hz, BW={Bandwidth} Hz, " +
                "Modulation={Modulation}, AudioDecim={Factor}:1",
                _sdrSampleRate, bandwidth, _currentModulation,
                _sdrSampleRate / DefaultAudioSampleRate);

            // Demodulation (operates at the SDR sample rate)
            _demodulator = DemodulatorFactory.Create(_currentModulation);
            _demodulator.SampleRate = _sdrSampleRate;
            _demodulator.Bandwidth = bandwidth;

            // Audio decimation (SDR rate → 48 kHz) using multi-stage decimator
            if (_sdrSampleRate > DefaultAudioSampleRate)
            {
                _audioDecimator = new AudioDecimator(_sdrSampleRate, DefaultAudioSampleRate);
            }
            else
            {
                _audioDecimator = null;
            }

            // Skip the post-decimation audio LPF — the multi-stage decimation
            // filters already handle anti-aliasing adequately.
            _audioFilter = null;

            _agc.Reset();
        }

        /// <summary>
        /// Calculates the optimal SDR sample rate for the given channel bandwidth.
        /// Picks a multiple of the audio rate that provides enough oversampling
        /// for the audio decimation filters to work cleanly.
        /// </summary>
        private static int CalculateSdrSampleRate(int channelBandwidthHz)
        {
            // Use ~5× bandwidth for comfortable filter margin.
            // At 5× oversampling, the audio content occupies ~20% of the bandwidth,
            // giving the decimation filter plenty of room for its transition band.
            var minRate = channelBandwidthHz * 5;
            const int minDeviceRate = 225_000; // RTL-SDR practical minimum
            minRate = Math.Max(minRate, minDeviceRate);

            // Find the smallest multiple of the audio rate that's ≥ minRate
            var multiplier = (int)Math.Ceiling((double)minRate / DefaultAudioSampleRate);
            var rate = multiplier * DefaultAudioSampleRate;

            // Clamp to the RTL-SDR's supported range
            return Math.Clamp(rate, minDeviceRate, 3_200_000);
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
            // Calculate signal strength from raw IQ
            var signalPower = samples.Average(s => s.MagnitudeSquared);
            _lastSignalStrength = MathF.Sqrt(signalPower);

            SignalStrengthUpdated?.Invoke(this, new SignalStrengthEventArgs(_lastSignalStrength));

            // Check squelch
            var squelchOpen = _lastSignalStrength >= _squelchThreshold;
            if (!squelchOpen || _isMuted)
            {
                return;
            }

            // 1. FM demodulation (IQ → mono audio at SDR sample rate)
            var audioSamples = new float[samples.Length];
            var demodCount = _demodulator.Demodulate(samples, audioSamples);

            // 2. Audio decimation (SDR rate → 48 kHz)
            if (_audioDecimator != null)
            {
                var decimatedAudio = new float[demodCount / _audioDecimator.Factor + 1];
                var decimatedCount = _audioDecimator.Decimate(
                    audioSamples.AsSpan(0, demodCount), decimatedAudio);
                audioSamples = new float[decimatedCount];
                Array.Copy(decimatedAudio, audioSamples, decimatedCount);
            }

            // 3. Audio low-pass filter (remove residual HF noise)
            if (_audioFilter != null)
            {
                var filtered = new float[audioSamples.Length];
                _audioFilter.Process(audioSamples, filtered);
                audioSamples = filtered;
            }

            // 4. Apply volume and clamp (FM demod gain already normalizes to ~[-1,1])
            if (_volume != 1.0f)
            {
                for (var i = 0; i < audioSamples.Length; i++)
                {
                    audioSamples[i] *= _volume;
                }
            }

            for (var i = 0; i < audioSamples.Length; i++)
            {
                audioSamples[i] = Math.Clamp(audioSamples[i], -1.0f, 1.0f);
            }

            // Notify listeners
            AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(audioSamples, _audioFormat));
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
    /// Event arguments for audio data
    /// </summary>
    public class AudioDataEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the audio samples
        /// </summary>
        public float[] Samples { get; }

        /// <summary>
        /// Gets the audio format
        /// </summary>
        public AudioFormat Format { get; }

        /// <summary>
        /// Creates new audio data event args
        /// </summary>
        /// <param name="samples">Audio samples</param>
        /// <param name="format">Audio format</param>
        public AudioDataEventArgs(float[] samples, AudioFormat format)
        {
            Samples = samples;
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
