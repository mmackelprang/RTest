using RTLSDRCore.Enums;
using RTLSDRCore.Models;
using Serilog;

namespace RTLSDRCore.Hardware;

  /// <summary>
  /// Mock SDR device for testing and development without physical hardware
  /// </summary>
  public class MockSdrDevice : ISdrDevice
  {
      private static readonly ILogger Logger = Log.ForContext<MockSdrDevice>();

      private readonly DeviceInfo _deviceInfo;
      private long _frequency = 100_000_000; // 100 MHz
      private int _sampleRate = 2_400_000;
      private float _gain = 20.0f;
      private bool _autoGain = true;
      private int _ppmCorrection = 0;
      private bool _isOpen;
      private bool _isStreaming;
      private bool _isHfMode;
      private CancellationTokenSource? _streamingCts;
      private Task? _streamingTask;
      private readonly Random _random = new();

      // Signal simulation parameters
      private readonly Dictionary<long, float> _simulatedSignals = new();
      private float _noiseFloor = 0.01f;

      /// <inheritdoc/>
      public DeviceInfo DeviceInfo => _deviceInfo;

      /// <inheritdoc/>
      public bool IsOpen => _isOpen;

      /// <inheritdoc/>
      public bool IsStreaming => _isStreaming;

      /// <inheritdoc/>
      public bool SupportsHfDirectSampling => _deviceInfo.HfDirectSampling != DirectSamplingMode.Off;

      /// <inheritdoc/>
      public bool IsHfMode => _isOpen && _isHfMode;

      /// <inheritdoc/>
      public event EventHandler<IqSamplesEventArgs>? SamplesAvailable;

      /// <inheritdoc/>
      public event EventHandler<DeviceErrorEventArgs>? ErrorOccurred;

      /// <summary>
      /// Creates a new mock SDR device with default settings
      /// </summary>
      public MockSdrDevice() : this(CreateDefaultDeviceInfo())
      {
      }

      /// <summary>
      /// Creates a new mock SDR device with specified device info
      /// </summary>
      /// <param name="deviceInfo">Device information</param>
      public MockSdrDevice(DeviceInfo deviceInfo)
      {
          _deviceInfo = deviceInfo;
          InitializeSimulatedSignals();
      }

      /// <summary>
      /// The mock's default device information: a simulated dongle with HF direct sampling on the
      /// Q branch (AUD-99), so its lower limit is <see cref="HfDirectSampling.HfMinFrequencyHz"/>.
      /// </summary>
      public static DeviceInfo CreateDefaultDeviceInfo()
      {
          return new DeviceInfo
          {
              Index = -1,
              Name = "Mock SDR Device",
              Manufacturer = "RTLSDRCore",
              Product = "Mock SDR",
              Serial = "MOCK-001",
              Type = DeviceType.Mock,
              IsAvailable = true,
              TunerType = "Simulated",
              HfDirectSampling = DirectSamplingMode.Q,
              MinFrequencyHz = HfDirectSampling.HfMinFrequencyHz,
              MaxFrequencyHz = 1_766_000_000,
              SupportedSampleRates = new[] { 250000, 1024000, 1536000, 1792000, 1920000, 2048000, 2400000, 2560000, 2880000, 3200000 },
              AvailableGains = new[] { 0f, 0.9f, 1.4f, 2.7f, 3.7f, 7.7f, 8.7f, 12.5f, 14.4f, 15.7f, 16.6f, 19.7f, 20.7f, 22.9f, 25.4f, 28.0f, 29.7f, 32.8f, 33.8f, 36.4f, 37.2f, 38.6f, 40.2f, 42.1f, 43.4f, 43.9f, 44.5f, 48.0f, 49.6f }
          };
      }

      private void InitializeSimulatedSignals()
      {
          // Add some simulated broadcast stations
          // FM Broadcast
          _simulatedSignals[88_100_000] = 0.8f;  // 88.1 FM
          _simulatedSignals[91_500_000] = 0.6f;  // 91.5 FM
          _simulatedSignals[94_700_000] = 0.9f;  // 94.7 FM
          _simulatedSignals[98_500_000] = 0.7f;  // 98.5 FM
          _simulatedSignals[101_100_000] = 0.85f; // 101.1 FM
          _simulatedSignals[104_300_000] = 0.75f; // 104.3 FM

          // AM Broadcast
          _simulatedSignals[680_000] = 0.5f;   // 680 AM
          _simulatedSignals[850_000] = 0.6f;   // 850 AM
          _simulatedSignals[1010_000] = 0.55f; // 1010 AM

          // Aircraft band
          _simulatedSignals[118_000_000] = 0.4f;  // Tower
          _simulatedSignals[121_500_000] = 0.3f;  // Guard
          _simulatedSignals[125_800_000] = 0.45f; // Approach

          // Weather band
          _simulatedSignals[162_400_000] = 0.7f;  // WX1
          _simulatedSignals[162_475_000] = 0.65f; // WX2
          _simulatedSignals[162_550_000] = 0.6f;  // WX3
      }

      /// <summary>
      /// Adds a simulated signal at a specific frequency
      /// </summary>
      /// <param name="frequencyHz">Frequency in Hz</param>
      /// <param name="strength">Signal strength (0.0 to 1.0)</param>
      public void AddSimulatedSignal(long frequencyHz, float strength)
      {
          _simulatedSignals[frequencyHz] = Math.Clamp(strength, 0f, 1f);
          Logger.Debug("Added simulated signal at {Frequency} with strength {Strength}",
              RadioBand.FormatFrequency(frequencyHz), strength);
      }

      /// <summary>
      /// Removes a simulated signal
      /// </summary>
      /// <param name="frequencyHz">Frequency in Hz</param>
      public void RemoveSimulatedSignal(long frequencyHz)
      {
          _simulatedSignals.Remove(frequencyHz);
      }

      /// <summary>
      /// Sets the noise floor level
      /// </summary>
      /// <param name="level">Noise level (0.0 to 1.0)</param>
      public void SetNoiseFloor(float level)
      {
          _noiseFloor = Math.Clamp(level, 0f, 1f);
      }

      /// <inheritdoc/>
      public bool Open()
      {
          if (_isOpen)
          {
              Logger.Warning("Device is already open");
              return false;
          }

          Logger.Information("Opening mock SDR device: {DeviceName}", _deviceInfo.Name);
          _isOpen = true;
          return true;
      }

      /// <inheritdoc/>
      public void Close()
      {
          if (!_isOpen)
    {
      return;
    }

    Logger.Information("Closing mock SDR device");
          StopStreaming();
          _isOpen = false;
          _isHfMode = false;
      }

      /// <inheritdoc/>
      public bool SetHfMode(bool on)
      {
          if (!on)
          {
              _isHfMode = false;
              return true;
          }

          if (!_isOpen || !SupportsHfDirectSampling)
          {
              return false;
          }

          _isHfMode = true;
          Logger.Debug("Mock HF direct sampling on ({Mode} branch)", _deviceInfo.HfDirectSampling);
          return true;
      }

      /// <summary>
      /// The lowest frequency <see cref="SetFrequency"/> accepts now: <see cref="DeviceInfo.MinFrequencyHz"/>
      /// in HF mode, or on a device without HF support; otherwise the tuner path's
      /// <see cref="RtlSdrDevice.TunerMinFrequencyHz"/> (or the device minimum, if higher).
      /// </summary>
      private long CurrentMinFrequencyHz =>
          _isHfMode || !SupportsHfDirectSampling
              ? _deviceInfo.MinFrequencyHz
              : Math.Max(_deviceInfo.MinFrequencyHz, RtlSdrDevice.TunerMinFrequencyHz);

      /// <inheritdoc/>
      public bool SetFrequency(long frequencyHz)
      {
          long min = CurrentMinFrequencyHz;
          if (frequencyHz < min || frequencyHz > _deviceInfo.MaxFrequencyHz)
          {
              Logger.Warning("Frequency {Frequency} out of range [{Min} - {Max}]",
                  RadioBand.FormatFrequency(frequencyHz),
                  RadioBand.FormatFrequency(min),
                  RadioBand.FormatFrequency(_deviceInfo.MaxFrequencyHz));
              return false;
          }

          _frequency = frequencyHz;
          Logger.Debug("Set frequency to {Frequency}", RadioBand.FormatFrequency(frequencyHz));
          return true;
      }

      /// <inheritdoc/>
      public long GetFrequency() => _frequency;

      /// <inheritdoc/>
      public bool SetSampleRate(int sampleRate)
      {
          // librtlsdr also accepts any rate in (225 kHz, 300 kHz], which is where the receiver's
          // own rates fall (240 kHz). Rejecting them made Startup fail once its result was checked (AUD-99).
          bool lowRange = sampleRate > 225_000 && sampleRate <= 300_000;
          if (!lowRange && !_deviceInfo.SupportedSampleRates.Contains(sampleRate))
          {
              Logger.Warning("Sample rate {SampleRate} not supported", sampleRate);
              return false;
          }

          _sampleRate = sampleRate;
          Logger.Debug("Set sample rate to {SampleRate}", sampleRate);
          return true;
      }

      /// <inheritdoc/>
      public int GetSampleRate() => _sampleRate;

      /// <inheritdoc/>
      public bool SetGainMode(bool automatic)
      {
          if (IsHfMode)
          {
              // As the real device: the tuner is in standby under direct sampling.
              Logger.Debug("Mock gain mode not set: HF direct sampling is on");
              return true;
          }

          _autoGain = automatic;
          Logger.Debug("Set gain mode to {Mode}", automatic ? "automatic" : "manual");
          return true;
      }

      /// <inheritdoc/>
      public bool SetGain(float gainDb)
      {
          if (IsHfMode)
          {
              Logger.Debug("Mock gain not set: HF direct sampling is on");
              return true;
          }

          // Find nearest supported gain value
          var nearest = _deviceInfo.AvailableGains.OrderBy(g => Math.Abs(g - gainDb)).First();
          _gain = nearest;
          Logger.Debug("Set gain to {Gain} dB (requested {Requested} dB)", nearest, gainDb);
          return true;
      }

      /// <inheritdoc/>
      public float GetGain() => _gain;

      /// <inheritdoc/>
      public bool SetFrequencyCorrection(int ppm)
      {
          _ppmCorrection = ppm;
          Logger.Debug("Set frequency correction to {PPM} ppm", ppm);
          return true;
      }

      /// <inheritdoc/>
      public int GetFrequencyCorrection() => _ppmCorrection;

      /// <inheritdoc/>
      public bool StartStreaming()
      {
          if (!_isOpen)
          {
              Logger.Error("Cannot start streaming: device not open");
              return false;
          }

          if (_isStreaming)
          {
              Logger.Warning("Already streaming");
              return false;
          }

          Logger.Information("Starting mock sample streaming");
          _isStreaming = true;
          _streamingCts = new CancellationTokenSource();

          _streamingTask = Task.Run(() => StreamingLoop(_streamingCts.Token));

          return true;
      }

      /// <inheritdoc/>
      public void StopStreaming()
      {
          if (!_isStreaming)
    {
      return;
    }

    Logger.Information("Stopping mock sample streaming");
          _streamingCts?.Cancel();
          _streamingTask?.Wait(TimeSpan.FromSeconds(1));
          _streamingCts?.Dispose();
          _streamingCts = null;
          _isStreaming = false;
      }

      private async Task StreamingLoop(CancellationToken ct)
      {
          const int bufferSize = 16384;
          var samples = new IqSample[bufferSize];
          var phase = 0.0;

          // Calculate delay for target sample rate
          var samplesPerSecond = _sampleRate;
          var delayMs = (int)(1000.0 * bufferSize / samplesPerSecond);

          while (!ct.IsCancellationRequested)
          {
              try
              {
                  GenerateSamples(samples, ref phase);
                  SamplesAvailable?.Invoke(this, new IqSamplesEventArgs(samples));
                  await Task.Delay(Math.Max(1, delayMs), ct);
              }
              catch (OperationCanceledException)
              {
                  break;
              }
              catch (Exception ex)
              {
                  Logger.Error(ex, "Error in streaming loop");
                  ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs("Streaming error", ex));
              }
          }
      }

      private void GenerateSamples(IqSample[] buffer, ref double phase)
      {
          var signalStrength = GetSignalStrengthAtFrequency(_frequency);
          var signalFrequency = 1000.0; // 1kHz tone for simulation
          var phaseIncrement = 2.0 * Math.PI * signalFrequency / _sampleRate;
          var gainFactor = _gain / 50.0f; // Normalize gain

          for (var i = 0; i < buffer.Length; i++)
          {
              // Generate signal
              var signal = (float)(signalStrength * Math.Sin(phase) * gainFactor);
              var quadrature = (float)(signalStrength * Math.Cos(phase) * gainFactor);

              // Add noise
              var noiseI = (float)((_random.NextDouble() * 2 - 1) * _noiseFloor);
              var noiseQ = (float)((_random.NextDouble() * 2 - 1) * _noiseFloor);

              buffer[i] = new IqSample(signal + noiseI, quadrature + noiseQ);

              phase += phaseIncrement;
              if (phase >= 2.0 * Math.PI)
      {
        phase -= 2.0 * Math.PI;
      }
    }
      }

      /// <summary>
      /// Gets the simulated signal strength at a frequency
      /// </summary>
      /// <param name="frequencyHz">Frequency in Hz</param>
      /// <returns>Signal strength (0.0 to 1.0)</returns>
      public float GetSignalStrengthAtFrequency(long frequencyHz)
      {
          float strength = 0;

          foreach (var kvp in _simulatedSignals)
          {
              var distance = Math.Abs(frequencyHz - kvp.Key);
              // Simulate signal spreading - stronger signals have wider bandwidth
              var bandwidth = kvp.Value * 100_000; // Up to 100kHz for strong signals

              if (distance < bandwidth)
              {
                  var attenuation = 1.0f - (distance / bandwidth);
                  strength = Math.Max(strength, kvp.Value * attenuation);
              }
          }

          return strength;
      }

      /// <inheritdoc/>
      public int ReadSamples(Span<IqSample> buffer)
      {
          // For synchronous reads, just generate noise/signal
          var phase = 0.0;
          var tempBuffer = new IqSample[buffer.Length];
          GenerateSamples(tempBuffer, ref phase);
          tempBuffer.CopyTo(buffer);
          return buffer.Length;
      }

      /// <inheritdoc/>
      public void Dispose()
      {
          Close();
          GC.SuppressFinalize(this);
      }
  }
