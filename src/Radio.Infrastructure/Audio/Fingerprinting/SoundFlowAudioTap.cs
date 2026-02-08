using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Captures audio samples from the SoundFlow output stream for fingerprinting.
/// Uses an independent stream reader so fingerprinting does not consume data
/// needed by HTTP stream clients (Chromecast).
/// </summary>
public sealed class SoundFlowAudioTap : IAudioSampleProvider
{
  private readonly ILogger<SoundFlowAudioTap> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioManager _audioManager;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowAudioTap"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="audioEngine">The audio engine.</param>
  /// <param name="audioManager">The audio manager for active source state.</param>
  public SoundFlowAudioTap(
    ILogger<SoundFlowAudioTap> logger,
    IAudioEngine audioEngine,
    IAudioManager audioManager)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
  }

  /// <inheritdoc/>
  public string SourceName => "SoundFlow Output";

  /// <inheritdoc/>
  public PlaySource SourceType
  {
    get
    {
      var activeSource = _audioManager.ActiveSource;
      if (activeSource == null) return PlaySource.GenericUSB;
      return activeSource.Type switch
      {
        AudioSourceType.Radio => PlaySource.Radio,
        AudioSourceType.FilePlayer => PlaySource.File,
        AudioSourceType.Vinyl => PlaySource.Vinyl,
        AudioSourceType.Bluetooth => PlaySource.Bluetooth,
        _ => PlaySource.GenericUSB
      };
    }
  }

  /// <inheritdoc/>
  public string? SourceFilePath
  {
    get
    {
      if (_audioManager.ActiveSource is FilePlayerAudioSource fileSource)
        return fileSource.CurrentFile;
      return null;
    }
  }

  /// <inheritdoc/>
  public bool IsActive
  {
    get
    {
      // Engine must be running AND an active source must be actually playing
      if (_audioEngine.State != AudioEngineState.Running)
        return false;

      var activeSource = _audioManager.ActiveSource;
      return activeSource?.State == AudioSourceState.Playing;
    }
  }

  /// <inheritdoc/>
  public async Task<AudioSampleBuffer?> CaptureAsync(TimeSpan duration, CancellationToken ct = default)
  {
    if (!IsActive)
    {
      _logger.LogDebug(
        "Cannot capture: engine state={EngineState}, active source={Source}, source state={SourceState}",
        _audioEngine.State,
        _audioManager.ActiveSource?.Name ?? "none",
        _audioManager.ActiveSource?.State.ToString() ?? "N/A");
      return null;
    }

    _logger.LogInformation("Capturing {Duration}s of audio from SoundFlow output", duration.TotalSeconds);
    var captureStartTime = DateTime.UtcNow;

    try
    {
      // Create an independent reader so we don't compete with HTTP stream clients
      using var stream = _audioEngine.CreateStreamReader("fingerprint-tap");

      _logger.LogDebug("Created independent stream reader for fingerprinting");

      // Get stream info (assume 48kHz stereo from TappedOutputStream)
      const int sampleRate = 48000;
      const int channels = 2;
      const int bytesPerSample = 2; // 16-bit PCM

      var totalSamples = (int)(duration.TotalSeconds * sampleRate * channels);
      var bytesToRead = totalSamples * bytesPerSample;
      _logger.LogDebug("Expecting to read {Bytes} bytes ({Samples} samples) for {Duration}s at {SampleRate}Hz {Channels}ch",
        bytesToRead, totalSamples, duration.TotalSeconds, sampleRate, channels);

      var buffer = new byte[bytesToRead];
      var bytesRead = 0;

      // Read samples over the duration with small intervals
      var readInterval = TimeSpan.FromMilliseconds(100);
      var stopwatch = System.Diagnostics.Stopwatch.StartNew();
      var readAttempts = 0;

      while (stopwatch.Elapsed < duration && bytesRead < bytesToRead && !ct.IsCancellationRequested)
      {
        var remaining = bytesToRead - bytesRead;
        var read = stream.Read(buffer, bytesRead, Math.Min(remaining, 4096));
        readAttempts++;

        if (read > 0)
        {
          bytesRead += read;
        }
        else
        {
          await Task.Delay(readInterval, ct);
        }
      }

      var captureElapsed = (DateTime.UtcNow - captureStartTime).TotalMilliseconds;

      if (bytesRead == 0)
      {
        _logger.LogWarning("No audio data captured after {Elapsed}ms and {Attempts} read attempts", captureElapsed, readAttempts);
        return null;
      }

      _logger.LogDebug("Read {BytesRead} bytes in {Attempts} attempts over {Elapsed}ms",
        bytesRead, readAttempts, captureElapsed);

      // Convert bytes to float samples
      var sampleCount = bytesRead / bytesPerSample;
      var samples = new float[sampleCount];
      for (int i = 0; i < sampleCount; i++)
      {
        var byteIndex = i * bytesPerSample;
        if (byteIndex + 1 < buffer.Length)
        {
          var pcm = (short)(buffer[byteIndex] | (buffer[byteIndex + 1] << 8));
          samples[i] = pcm / (float)short.MaxValue;
        }
      }

      // RMS silence check: skip if captured audio is below -60dB
      var sumSquares = 0.0;
      for (int i = 0; i < sampleCount; i++)
      {
        sumSquares += samples[i] * samples[i];
      }
      var rms = Math.Sqrt(sumSquares / sampleCount);
      var rmsDb = rms > 0 ? 20 * Math.Log10(rms) : -100;

      if (rmsDb < -60)
      {
        _logger.LogDebug("Captured audio is silence (RMS: {RmsDb:F1}dB), skipping identification", rmsDb);
        return null;
      }

      var actualDuration = (double)sampleCount / sampleRate / channels;
      _logger.LogInformation("Successfully captured {Samples} samples ({Duration:F2}s, {Percentage:F0}% of requested, RMS: {RmsDb:F1}dB) in {Elapsed}ms",
        sampleCount, actualDuration, (actualDuration / duration.TotalSeconds) * 100, rmsDb, captureElapsed);

      return new AudioSampleBuffer
      {
        Samples = samples,
        SampleRate = sampleRate,
        Channels = channels,
        Duration = TimeSpan.FromSeconds(actualDuration),
        SourceName = SourceName
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error capturing audio samples from SoundFlow output");
      return null;
    }
  }
}
