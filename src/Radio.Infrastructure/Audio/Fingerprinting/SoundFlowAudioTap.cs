using System.Buffers;
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

  // Reusable chunk buffer — avoids allocating a new byte[4096] per loop iteration
  private readonly byte[] _chunkBuffer = new byte[4096];

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
  public string SourceName => _audioManager.ActiveSource?.Name ?? "Unknown";

  /// <inheritdoc/>
  public PlaySource SourceType
  {
    get
    {
      var activeSource = _audioManager.ActiveSource;
      if (activeSource == null)
      {
        return PlaySource.GenericUSB;
      }
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
      {
        return fileSource.CurrentFile;
      }
      return null;
    }
  }

  /// <inheritdoc/>
  public bool NeedsFingerprintingLookup
  {
    get
    {
      var source = _audioManager.ActiveSource;
      if (source == null)
      {
        return false;
      }

      // Bluetooth has a direct property
      if (source is BluetoothAudioSource btSource)
      {
        return btSource.NeedsFingerprintingLookup;
      }

      // FilePlayer uses metadata dictionary
      if (source is FilePlayerAudioSource fileSource)
      {
        if (fileSource.Metadata?.TryGetValue("NeedsFingerprintingLookup", out var val) == true)
        {
          return val is bool b && b;
        }
        return true; // Default: needs fingerprinting if flag not set
      }

      // Radio, Vinyl, USB always need fingerprinting
      return true;
    }
  }

  /// <inheritdoc/>
  public bool IsActive
  {
    get
    {
      // Engine must be running AND an active source must be actually playing
      if (_audioEngine.State != AudioEngineState.Running)
      {
        return false;
      }

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

    _logger.LogDebug("Capturing {Duration}s of audio from SoundFlow output", duration.TotalSeconds);
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

      // Rent from ArrayPool to avoid LOH allocation (~2.7MB)
      var buffer = ArrayPool<byte>.Shared.Rent(bytesToRead);
      try
      {
      var bytesRead = 0;

      // IMPORTANT: Use ReadAsync with real-time pacing, not sync Read.
      // The ring buffer's ReadForReader returns silence (zeros) when no new
      // audio is available. Sync Read hammers the buffer at CPU speed, filling
      // the capture with 99% silence in milliseconds. ReadAsync paces silence
      // reads to approximate real-time, ensuring we capture actual audio over
      // the full duration. We also skip zero-only chunks to only accumulate
      // real audio data.
      var stopwatch = System.Diagnostics.Stopwatch.StartNew();
      var readAttempts = 0;
      var silenceChunks = 0;

      while (stopwatch.Elapsed < duration && bytesRead < bytesToRead && !ct.IsCancellationRequested)
      {
        var remaining = bytesToRead - bytesRead;
        var chunkSize = Math.Min(remaining, _chunkBuffer.Length);

        var read = await stream.ReadAsync(_chunkBuffer, 0, chunkSize, ct);
        readAttempts++;

        if (read > 0)
        {
          // Check if chunk contains actual audio (not all zeros from silence fill)
          bool hasAudio = false;
          for (int i = 0; i < read; i += 2)
          {
            if (i + 1 < read && (_chunkBuffer[i] != 0 || _chunkBuffer[i + 1] != 0))
            {
              hasAudio = true;
              break;
            }
          }

          if (hasAudio)
          {
            Buffer.BlockCopy(_chunkBuffer, 0, buffer, bytesRead, read);
            bytesRead += read;
          }
          else
          {
            silenceChunks++;
          }
        }
      }

      var captureElapsed = (DateTime.UtcNow - captureStartTime).TotalMilliseconds;
      _logger.LogDebug("Capture loop: {Attempts} reads, {SilenceChunks} silence chunks skipped, {BytesRead} audio bytes in {Elapsed}ms",
        readAttempts, silenceChunks, bytesRead, captureElapsed);

      if (bytesRead == 0)
      {
        _logger.LogWarning("No audio data captured after {Elapsed}ms and {Attempts} read attempts", captureElapsed, readAttempts);
        return null;
      }

      _logger.LogDebug("Read {BytesRead} bytes in {Attempts} attempts over {Elapsed}ms",
        bytesRead, readAttempts, captureElapsed);

      // RMS silence check on raw PCM shorts — avoids allocating a float[] (~5.5MB LOH)
      // when audio is silence (the common case during idle).
      var sampleCount = bytesRead / bytesPerSample;
      var sumSquares = 0.0;
      for (int i = 0; i < sampleCount; i++)
      {
        var byteIndex = i * bytesPerSample;
        if (byteIndex + 1 < bytesRead)
        {
          var pcm = (short)(buffer[byteIndex] | (buffer[byteIndex + 1] << 8));
          var normalized = pcm / (double)short.MaxValue;
          sumSquares += normalized * normalized;
        }
      }
      var rms = Math.Sqrt(sumSquares / sampleCount);
      var rmsDb = rms > 0 ? 20 * Math.Log10(rms) : -100;

      if (rmsDb < -60)
      {
        _logger.LogDebug("Captured audio is silence (RMS: {RmsDb:F1}dB), skipping identification", rmsDb);
        return null;
      }

      // Audio passed silence check — now convert bytes to float samples
      var samples = new float[sampleCount];
      for (int i = 0; i < sampleCount; i++)
      {
        var byteIndex = i * bytesPerSample;
        if (byteIndex + 1 < bytesRead)
        {
          var pcm = (short)(buffer[byteIndex] | (buffer[byteIndex + 1] << 8));
          samples[i] = pcm / (float)short.MaxValue;
        }
      }

      var actualDuration = (double)sampleCount / sampleRate / channels;
      _logger.LogDebug("Successfully captured {Samples} samples ({Duration:F2}s, {Percentage:F0}% of requested, RMS: {RmsDb:F1}dB) in {Elapsed}ms",
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
      finally
      {
        ArrayPool<byte>.Shared.Return(buffer);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error capturing audio samples from SoundFlow output");
      return null;
    }
  }
}
