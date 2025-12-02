using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Captures audio samples from the SoundFlow output stream for fingerprinting.
/// </summary>
public sealed class SoundFlowAudioTap : IAudioSampleProvider
{
  private readonly ILogger<SoundFlowAudioTap> _logger;
  private readonly IAudioEngine _audioEngine;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowAudioTap"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="audioEngine">The audio engine.</param>
  public SoundFlowAudioTap(
    ILogger<SoundFlowAudioTap> logger,
    IAudioEngine audioEngine)
  {
    _logger = logger;
    _audioEngine = audioEngine;
  }

  /// <inheritdoc/>
  public string SourceName => "SoundFlow Output";

  /// <inheritdoc/>
  public PlaySource SourceType => PlaySource.GenericUSB;

  /// <inheritdoc/>
  public bool IsActive => _audioEngine.State == AudioEngineState.Running;

  /// <inheritdoc/>
  public async Task<AudioSampleBuffer?> CaptureAsync(TimeSpan duration, CancellationToken ct = default)
  {
    if (!IsActive)
    {
      _logger.LogDebug("Audio engine not running, cannot capture samples");
      return null;
    }

    _logger.LogDebug("Capturing {Duration}s of audio from SoundFlow output", duration.TotalSeconds);

    try
    {
      var stream = _audioEngine.GetMixedOutputStream();
      if (stream == null)
      {
        _logger.LogWarning("Mixed output stream not available");
        return null;
      }

      // Get stream info (assume 48kHz stereo from TappedOutputStream)
      const int sampleRate = 48000;
      const int channels = 2;
      const int bytesPerSample = 2; // 16-bit PCM

      var totalSamples = (int)(duration.TotalSeconds * sampleRate * channels);
      var bytesToRead = totalSamples * bytesPerSample;
      var buffer = new byte[bytesToRead];
      var bytesRead = 0;

      // Read samples over the duration with small intervals
      var readInterval = TimeSpan.FromMilliseconds(100);
      var stopwatch = System.Diagnostics.Stopwatch.StartNew();

      while (stopwatch.Elapsed < duration && bytesRead < bytesToRead && !ct.IsCancellationRequested)
      {
        var remaining = bytesToRead - bytesRead;
        var read = stream.Read(buffer, bytesRead, Math.Min(remaining, 4096));
        if (read > 0)
        {
          bytesRead += read;
        }
        else
        {
          await Task.Delay(readInterval, ct);
        }
      }

      if (bytesRead == 0)
      {
        _logger.LogWarning("No audio data captured");
        return null;
      }

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

      _logger.LogDebug("Captured {Samples} samples ({Duration}s) from SoundFlow output",
        sampleCount, (double)sampleCount / sampleRate / channels);

      return new AudioSampleBuffer
      {
        Samples = samples,
        SampleRate = sampleRate,
        Channels = channels,
        Duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate / channels),
        SourceName = SourceName
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error capturing audio samples");
      return null;
    }
  }
}
