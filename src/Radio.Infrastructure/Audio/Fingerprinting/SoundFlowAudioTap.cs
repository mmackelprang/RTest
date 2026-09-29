using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting.Services;
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
  private readonly FingerprintCaptureWatchdog? _captureWatchdog;
  private readonly IBluetoothService? _bluetoothService;

  // Reusable chunk buffer — avoids allocating a new byte[4096] per loop iteration
  private readonly byte[] _chunkBuffer = new byte[4096];

  // Reusable capture buffers — eliminate the ~7 MB of Large Object Heap churn that
  // occurred on every fingerprint cycle (~15 s) and triggered a GC pause which
  // starved the capture-fill thread (root cause of Cast-output underruns to
  // 0/384000). Captures are strictly serial: BackgroundIdentificationService runs a
  // single identification loop, and RequestImmediateIdentification only cancels the
  // backoff delay — it never starts a concurrent cycle. This matches the existing
  // single-threaded _chunkBuffer reuse assumption.
  //
  // _captureBuffer is internal scratch (never escapes this method) and is grow-only.
  // _sampleBuffer escapes via AudioSampleBuffer.Samples, whose Length is load-bearing
  // (SongRec writes exactly Samples.Length samples), so it is reused only on an exact
  // length match and otherwise reallocated. For a steady, non-silent stream the
  // captured sampleCount is constant, so it is allocated once and reused thereafter.
  private byte[] _captureBuffer = Array.Empty<byte>();
  private float[]? _sampleBuffer;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowAudioTap"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="audioEngine">The audio engine.</param>
  /// <param name="audioManager">The audio manager for active source state.</param>
  /// <param name="captureWatchdog">
  /// AUD-18: told the outcome of every capture window; while it is latched this tap's per-window
  /// "no audio" Warning is logged at Debug. Optional so tests and hosts without it still construct.
  /// </param>
  /// <param name="bluetoothService">
  /// Used only to ask whether the Bluetooth capture pipeline is <see cref="BluetoothPipelineStatus.Healthy"/>
  /// when Bluetooth is the active source. Optional; without it that check is skipped.
  /// </param>
  public SoundFlowAudioTap(
    ILogger<SoundFlowAudioTap> logger,
    IAudioEngine audioEngine,
    IAudioManager audioManager,
    FingerprintCaptureWatchdog? captureWatchdog = null,
    IBluetoothService? bluetoothService = null)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _audioManager = audioManager;
    _captureWatchdog = captureWatchdog;
    _bluetoothService = bluetoothService;
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

  /// <summary>
  /// AUD-18: whether <paramref name="source"/> should be delivering audio to this tap right now — it is the
  /// active source, the engine is running, it is Playing, a fingerprint lookup is wanted, and, for
  /// Bluetooth, the capture pipeline reports <see cref="BluetoothPipelineStatus.Healthy"/>. Anything else
  /// (no phone connected, the capture node gone because the handset paused, a broken stream) is the
  /// Bluetooth pipeline's own state to report (PipelineStatus), and a tap with nothing upstream is not
  /// starving.
  /// </summary>
  private bool AudioShouldBeReachingTap(IAudioSource? source)
  {
    if (source == null
      || !ReferenceEquals(_audioManager.ActiveSource, source)
      || !IsActive
      || !NeedsFingerprintingLookup)
    {
      return false;
    }

    if (source.Type == AudioSourceType.Bluetooth
      && _bluetoothService != null
      && _bluetoothService.PipelineStatus != BluetoothPipelineStatus.Healthy)
    {
      return false;
    }

    return true;
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

    // AUD-18: what the watchdog needs to judge an empty window, taken at the START — the same checks are
    // repeated at the end, and the window only counts as "empty while playing" if both ends pass.
    var sourceAtStart = _audioManager.ActiveSource;
    var sourceNameAtStart = SourceName;
    var sourceTypeAtStart = SourceType;
    var audioExpectedAtStart = AudioShouldBeReachingTap(sourceAtStart);

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

      // Reuse a grow-only scratch buffer instead of renting from the shared
      // ArrayPool. The shared pool no-ops above ~1 MB, so it allocated and then
      // immediately discarded this ~2.3 MB buffer on the LOH every cycle. This
      // buffer never escapes the method, so grow-only reuse is safe.
      if (_captureBuffer.Length < bytesToRead)
      {
        _captureBuffer = new byte[bytesToRead];
      }
      var buffer = _captureBuffer;

      var bytesRead = 0;

      // IMPORTANT: Use ReadAsync, not sync Read (which blocks a thread while it waits).
      // While the source is playing, ReadAsync waits for the next block when it has
      // caught up (AUD-79). Only when the source has been silent for ~250 ms does the
      // ring buffer hand out keep-alive silence (zeros), and ReadAsync paces those reads
      // to approximate real-time. Zero-only chunks are skipped so only real audio
      // accumulates.
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
        var outcome = audioExpectedAtStart
          && !ct.IsCancellationRequested
          && AudioShouldBeReachingTap(sourceAtStart)
          ? CaptureWindowOutcome.EmptyWhilePlaying
          : CaptureWindowOutcome.EmptyNotPlaying;
        _captureWatchdog?.RecordWindow(
          outcome, sourceNameAtStart, sourceTypeAtStart, TimeSpan.FromMilliseconds(captureElapsed));

        // AUD-18: record first, so the window that trips the watchdog is reported by the watchdog's own
        // Warning rather than by this one as well. Every window before the trip keeps its Warning.
        var level = _captureWatchdog?.IsLatched == true ? LogLevel.Debug : LogLevel.Warning;
        _logger.Log(level, "No audio data captured after {Elapsed}ms and {Attempts} read attempts", captureElapsed, readAttempts);
        return null;
      }

      // Any non-zero chunk counts as audio here, including a capture the RMS check below then calls silence:
      // the tap is receiving samples, which is the thing the watchdog is watching.
      _captureWatchdog?.RecordWindow(
        CaptureWindowOutcome.Audio, sourceNameAtStart, sourceTypeAtStart, TimeSpan.FromMilliseconds(captureElapsed));

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

      // Audio passed silence check — now convert bytes to float samples.
      // Reuse the sample buffer when the length matches (steady stream → constant
      // sampleCount, allocated once and reused); otherwise reallocate so that
      // AudioSampleBuffer.Samples.Length stays exact. This removes the ~4.6 MB
      // float[] LOH allocation per fingerprint cycle.
      var samples = _sampleBuffer is { } reuse && reuse.Length == sampleCount
        ? reuse
        : (_sampleBuffer = new float[sampleCount]);
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
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error capturing audio samples from SoundFlow output");
      return null;
    }
  }
}
