using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A passthrough audio modifier that captures mixed audio samples and writes them
/// to the TappedOutputStream for fingerprinting and HTTP streaming.
/// </summary>
/// <remarks>
/// This modifier should be added to the playback device's MasterMixer to capture
/// all mixed audio output. It buffers samples and periodically writes them to
/// the output tap in batches for efficiency.
/// </remarks>
public class FingerprintTapModifier : BufferedTapModifier
{
  private readonly SoundFlowAudioEngine _audioEngine;
  private readonly ILogger? _logger;
  private readonly IMetricsCollector? _metricsCollector;
  private long _totalSamplesProcessed;
  private long _batchCount;
  private long _writeErrorCount;
  private long _lastReportedBatches;
  private long _lastReportedErrors;
  private bool _loggedFirstBatch;
  private DateTime _lastLogTime = DateTime.MinValue;
  private DateTime _lastProcessedTime = DateTime.MinValue;

  /// <summary>
  /// Initializes a new instance of the <see cref="FingerprintTapModifier"/> class.
  /// </summary>
  /// <param name="audioEngine">The audio engine to write samples to.</param>
  /// <param name="logger">Optional logger for diagnostic output.</param>
  /// <param name="bufferSize">Size of the sample buffer before writing to tap (default: 4096). Smaller values reduce latency but increase lock contention and GC pressure in the audio callback.</param>
  /// <param name="metricsCollector">Optional metrics collector for pipeline metrics.</param>
  public FingerprintTapModifier(
    SoundFlowAudioEngine audioEngine,
    ILogger? logger = null,
    int bufferSize = 4096,
    IMetricsCollector? metricsCollector = null)
    : base(bufferSize)
  {
    _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
    _logger = logger;
    _metricsCollector = metricsCollector;
    Name = "Fingerprint Tap";
  }

  /// <inheritdoc/>
  protected override void OnSampleBuffered()
  {
    _totalSamplesProcessed++;
  }

  /// <inheritdoc/>
  protected override void ProcessFlushBuffer(float[] buffer)
  {
    _audioEngine.WriteToOutputTap(buffer);
    _batchCount++;
    _lastProcessedTime = DateTime.UtcNow;

    if (!_loggedFirstBatch)
    {
      _loggedFirstBatch = true;
      _logger?.LogInformation(
        "FingerprintTap: First {BufferSize} samples written to output tap (total processed: {TotalSamples})",
        BufferSize, _totalSamplesProcessed);
    }

    if ((_lastProcessedTime - _lastLogTime).TotalSeconds >= 10)
    {
      _logger?.LogDebug(
        "FingerprintTap: {TotalSamples} samples processed, writing {BufferSize} to tap",
        _totalSamplesProcessed, BufferSize);
      _lastLogTime = _lastProcessedTime;

      if (_metricsCollector != null)
      {
        var batchDelta = _batchCount - _lastReportedBatches;
        if (batchDelta > 0)
        {
          _metricsCollector.Increment("audio.tap.batches_written", batchDelta);
          _lastReportedBatches = _batchCount;
        }

        var errorDelta = _writeErrorCount - _lastReportedErrors;
        if (errorDelta > 0)
        {
          _metricsCollector.Increment("audio.tap.write_errors", errorDelta);
          _lastReportedErrors = _writeErrorCount;
        }
      }
    }
  }

  /// <inheritdoc/>
  protected override void OnFlushError(Exception ex)
  {
    _writeErrorCount++;
    _logger?.LogWarning(ex, "Error writing samples to output tap");
  }

  /// <summary>
  /// Gets the total number of samples processed by this modifier.
  /// </summary>
  public long TotalSamplesProcessed => _totalSamplesProcessed;

  /// <summary>
  /// Gets the timestamp of the last sample processed by this modifier.
  /// </summary>
  public DateTime LastProcessedTime => _lastProcessedTime;
}
