using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Metrics;
using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Platform.Bluetooth.Windows;

/// <summary>
/// Bridges NAudio WASAPI loopback capture → SoundFlow BufferedSoundGenerator.
/// Captures audio from the Windows default render endpoint (pre-mute) and feeds
/// it into the SoundFlow pipeline for output device selection, modifiers, Cast, and visualization.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WasapiLoopbackCaptureSource : IDisposable
{
  private readonly ILogger _logger;
  private WasapiLoopbackCapture? _capture;
  private MMDevice? _defaultEndpoint;
  private bool _previousMuteState;
  private bool _endpointMuted;
  private bool _disposed;

  // Resampling state for when system sample rate != 48kHz
  private NAudio.Wave.SampleProviders.WdlResamplingSampleProvider? _resampler;
  private BufferedWaveProvider? _resampleBuffer;

  public WasapiLoopbackCaptureSource(ILogger logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Creates a BufferedSoundGenerator configured for the SoundFlow pipeline.
  /// Call this before StartCapture to get the component to register with the mixer.
  /// </summary>
  public BufferedSoundGenerator<float> CreateGenerator(AudioEngine engine, AudioFormat format, ILogger generatorLogger,
    IMetricsCollector? metricsCollector = null)
  {
    return new BufferedSoundGenerator<float>(engine, format, generatorLogger, metricsCollector: metricsCollector);
  }

  /// <summary>
  /// Starts WASAPI loopback capture, feeding samples into the provided generator.
  /// </summary>
  /// <param name="generator">The SoundFlow generator to feed samples into.</param>
  /// <param name="muteEndpoint">Whether to mute the default endpoint to prevent double-play.</param>
  public void StartCapture(BufferedSoundGenerator<float> generator, bool muteEndpoint)
  {
    if (_capture != null)
    {
      _logger.LogWarning("WASAPI loopback capture already running, stopping first");
      StopCapture();
    }

    try
    {
      // Get default render endpoint for loopback
      var enumerator = new MMDeviceEnumerator();
      _defaultEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

      _logger.LogInformation(
        "WASAPI loopback capture starting on endpoint: {DeviceName} (mute: {Mute})",
        _defaultEndpoint.FriendlyName, muteEndpoint);

      // Save and optionally mute the endpoint
      if (muteEndpoint)
      {
        _previousMuteState = _defaultEndpoint.AudioEndpointVolume.Mute;
        _defaultEndpoint.AudioEndpointVolume.Mute = true;
        _endpointMuted = true;
        _logger.LogInformation("Muted default render endpoint: {DeviceName} (previous mute: {PrevMute})",
          _defaultEndpoint.FriendlyName, _previousMuteState);
      }

      _capture = new WasapiLoopbackCapture(_defaultEndpoint);

      var captureFormat = _capture.WaveFormat;
      _logger.LogInformation(
        "WASAPI loopback format: {SampleRate}Hz, {Channels}ch, {BitsPerSample}bit, Encoding: {Encoding}",
        captureFormat.SampleRate, captureFormat.Channels, captureFormat.BitsPerSample, captureFormat.Encoding);

      // Set up resampling if the capture sample rate differs from 48kHz
      const int targetSampleRate = 48000;
      if (captureFormat.SampleRate != targetSampleRate)
      {
        _logger.LogInformation("WASAPI capture rate {CaptureRate}Hz != {TargetRate}Hz, enabling resampler",
          captureFormat.SampleRate, targetSampleRate);

        _resampleBuffer = new BufferedWaveProvider(captureFormat)
        {
          BufferLength = captureFormat.AverageBytesPerSecond * 2,
          DiscardOnBufferOverflow = true
        };

        var sampleProvider = _resampleBuffer.ToSampleProvider();
        _resampler = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(sampleProvider, targetSampleRate);
      }

      _capture.DataAvailable += (sender, e) => OnDataAvailable(e, generator);
      _capture.RecordingStopped += (sender, e) =>
      {
        if (e.Exception != null)
        {
          _logger.LogWarning(e.Exception, "WASAPI loopback capture stopped with error");
        }
        else
        {
          _logger.LogDebug("WASAPI loopback capture stopped");
        }
      };

      _capture.StartRecording();
      _logger.LogInformation("WASAPI loopback capture started");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start WASAPI loopback capture");
      StopCapture();
      throw;
    }
  }

  /// <summary>
  /// Stops capture and restores endpoint mute state.
  /// </summary>
  public void StopCapture()
  {
    try
    {
      _capture?.StopRecording();
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error stopping WASAPI loopback capture");
    }

    _capture?.Dispose();
    _capture = null;
    _resampler = null;
    _resampleBuffer = null;

    // Restore endpoint mute state
    if (_endpointMuted && _defaultEndpoint != null)
    {
      try
      {
        _defaultEndpoint.AudioEndpointVolume.Mute = _previousMuteState;
        _logger.LogInformation("Restored default endpoint mute state: {MuteState}", _previousMuteState);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to restore default endpoint mute state");
      }
      _endpointMuted = false;
    }

    _defaultEndpoint = null;
  }

  /// <summary>
  /// Checks whether the SoundFlow selected output device is the same as the Windows default render endpoint.
  /// If same, muting would silence SoundFlow's output too — must NOT mute.
  /// </summary>
  public bool IsSameDeviceAsDefault(string? soundFlowDeviceId)
  {
    if (string.IsNullOrEmpty(soundFlowDeviceId))
    {
      return true; // Default device selected → same device
    }

    try
    {
      var enumerator = new MMDeviceEnumerator();
      var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
      var defaultName = defaultDevice.FriendlyName;

      // Compare by name substring (SoundFlow device IDs may differ from MMDevice IDs)
      var isSame = soundFlowDeviceId.Contains(defaultName, StringComparison.OrdinalIgnoreCase)
                   || defaultName.Contains(soundFlowDeviceId, StringComparison.OrdinalIgnoreCase);

      _logger.LogDebug(
        "Same-device check: SoundFlow='{SoundFlowDevice}', WinDefault='{WinDefault}', IsSame={IsSame}",
        soundFlowDeviceId, defaultName, isSame);

      return isSame;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error checking same-device for WASAPI loopback");
      return true; // Assume same device on error (safer — don't mute)
    }
  }

  private void OnDataAvailable(WaveInEventArgs e, BufferedSoundGenerator<float> generator)
  {
    if (e.BytesRecorded == 0)
    {
      return;
    }

    try
    {
      if (_resampler != null && _resampleBuffer != null)
      {
        // Feed through resampler
        _resampleBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Read resampled float samples
        var outputBuffer = new float[e.BytesRecorded / 4]; // Approximate output size
        int samplesRead = _resampler.Read(outputBuffer, 0, outputBuffer.Length);
        if (samplesRead > 0)
        {
          if (samplesRead < outputBuffer.Length)
          {
            var trimmed = new float[samplesRead];
            Array.Copy(outputBuffer, trimmed, samplesRead);
            generator.AddSamples(trimmed);
          }
          else
          {
            generator.AddSamples(outputBuffer);
          }
        }
      }
      else
      {
        // No resampling needed — convert bytes directly to float
        // WASAPI loopback delivers IEEE float by default
        var floatCount = e.BytesRecorded / 4;
        var samples = new float[floatCount];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        // If stereo -> stereo, just push directly
        generator.AddSamples(samples);
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error processing WASAPI loopback audio data");
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;
    StopCapture();
  }
}
