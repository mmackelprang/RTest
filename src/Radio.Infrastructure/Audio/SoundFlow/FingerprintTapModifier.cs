using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces;
using SoundFlow.Abstracts;

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
public class FingerprintTapModifier : SoundModifier
{
  private readonly SoundFlowAudioEngine _audioEngine;
  private readonly ILogger? _logger;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly float[] _sampleBuffer;
  private readonly int _bufferSize;
  private int _bufferIndex;
  private readonly object _lock = new();
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
  {
    _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
    _logger = logger;
    _metricsCollector = metricsCollector;
    _bufferSize = bufferSize;
    _sampleBuffer = new float[bufferSize];
    _bufferIndex = 0;
    Name = "Fingerprint Tap";
  }

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    // Hot path: called 96,000 times/second for stereo 48kHz.
    // Minimize work inside the lock — no DateTime, no allocations.
    lock (_lock)
    {
      _totalSamplesProcessed++;

      if (_bufferIndex < _bufferSize)
      {
        _sampleBuffer[_bufferIndex++] = sample;
      }

      // When buffer is full, write to output tap and reset
      if (_bufferIndex >= _bufferSize)
      {
        _lastProcessedTime = DateTime.UtcNow;

        try
        {
          // Create a copy to write to the tap
          var samplesForTap = new float[_bufferSize];
          Array.Copy(_sampleBuffer, samplesForTap, _bufferSize);

          // Write to the output tap for fingerprinting/streaming
          _audioEngine.WriteToOutputTap(samplesForTap);
          _batchCount++;

          // Log first batch at Information level for diagnostics
          if (!_loggedFirstBatch)
          {
            _loggedFirstBatch = true;
            _logger?.LogInformation(
              "FingerprintTap: First {BufferSize} samples written to output tap (total processed: {TotalSamples})",
              _bufferSize, _totalSamplesProcessed);
          }

          // Log periodically (every 10 seconds)
          if ((_lastProcessedTime - _lastLogTime).TotalSeconds >= 10)
          {
            _logger?.LogDebug(
              "FingerprintTap: {TotalSamples} samples processed, writing {BufferSize} to tap",
              _totalSamplesProcessed, _bufferSize);
            _lastLogTime = _lastProcessedTime;

            // Report metrics
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
        catch (Exception ex)
        {
          _writeErrorCount++;
          _logger?.LogWarning(ex, "Error writing samples to output tap");
        }

        _bufferIndex = 0;
      }
    }

    // Pass through unchanged - this is a tap, not an effect
    return sample;
  }

  /// <summary>
  /// Flushes any remaining samples in the buffer to the output tap.
  /// </summary>
  public void Flush()
  {
    lock (_lock)
    {
      if (_bufferIndex > 0)
      {
        try
        {
          var remainingSamples = new float[_bufferIndex];
          Array.Copy(_sampleBuffer, remainingSamples, _bufferIndex);
          _audioEngine.WriteToOutputTap(remainingSamples);
        }
        catch
        {
          // Ignore flush errors
        }

        _bufferIndex = 0;
      }
    }
  }

  /// <summary>
  /// Resets the sample buffer.
  /// </summary>
  public void Reset()
  {
    lock (_lock)
    {
      _bufferIndex = 0;
      Array.Clear(_sampleBuffer, 0, _bufferSize);
    }
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
