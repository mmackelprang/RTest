using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Audio.Validation;

/// <summary>
/// Validates audio levels (RMS + peak per channel).
/// Works with any audio source, not just diagnostic tones.
/// Useful for diagnosing quiet/silent audio regardless of content.
/// </summary>
public sealed class LevelValidator : IAudioValidator, IDisposable
{
  private readonly ILogger<LevelValidator> _logger;
  private readonly AudioValidationOptions _options;
  private readonly ConcurrentQueue<AnalysisBatch> _queue = new();
  private readonly Thread _analysisThread;
  private readonly CancellationTokenSource _cts = new();
  private readonly ManualResetEventSlim _workAvailable = new(false);
  private readonly int _channels;
  private int _batchCount;

  public LevelValidator(
    ILogger<LevelValidator> logger,
    AudioValidationOptions options,
    int channels = 2)
  {
    _logger = logger;
    _options = options;
    _channels = channels;

    _analysisThread = new Thread(AnalysisLoop)
    {
      Name = "AudioValidator-Level",
      IsBackground = true,
      Priority = ThreadPriority.BelowNormal
    };
    _analysisThread.Start();
  }

  /// <inheritdoc/>
  public void Submit(ReadOnlySpan<float> samples, string stageName)
  {
    var copy = samples.ToArray();
    _queue.Enqueue(new AnalysisBatch(copy, stageName));
    _workAvailable.Set();
  }

  /// <inheritdoc/>
  public Task FlushAsync(CancellationToken cancellationToken = default)
  {
    _cts.Cancel();
    _workAvailable.Set();

    while (_queue.TryDequeue(out var batch))
    {
      AnalyzeBatch(batch);
    }

    return Task.CompletedTask;
  }

  private void AnalysisLoop()
  {
    while (!_cts.IsCancellationRequested)
    {
      _workAvailable.Wait(_cts.Token);
      _workAvailable.Reset();

      while (_queue.TryDequeue(out var batch))
      {
        if (_cts.IsCancellationRequested) break;
        AnalyzeBatch(batch);
      }
    }
  }

  private void AnalyzeBatch(AnalysisBatch batch)
  {
    var samples = batch.Samples;
    var stage = batch.StageName;

    if (_channels < 1 || samples.Length < _channels)
      return;

    var frames = samples.Length / _channels;
    var rmsPerChannel = new float[_channels];
    var peakPerChannel = new float[_channels];

    // Calculate per-channel RMS and peak
    for (var ch = 0; ch < _channels; ch++)
    {
      var sum = 0.0;
      var peak = 0f;

      for (var i = 0; i < frames; i++)
      {
        var sample = samples[i * _channels + ch];
        sum += sample * (double)sample;
        var abs = Math.Abs(sample);
        if (abs > peak) peak = abs;
      }

      rmsPerChannel[ch] = frames > 0 ? (float)Math.Sqrt(sum / frames) : 0f;
      peakPerChannel[ch] = peak;
    }

    // Convert to dB
    var leftRmsDb = ToDb(rmsPerChannel[0]);
    var rightRmsDb = _channels > 1 ? ToDb(rmsPerChannel[1]) : float.NegativeInfinity;
    var leftPeakDb = ToDb(peakPerChannel[0]);
    var rightPeakDb = _channels > 1 ? ToDb(peakPerChannel[1]) : float.NegativeInfinity;

    // Check for silence
    if (leftRmsDb < _options.SilenceThresholdDb && rightRmsDb < _options.SilenceThresholdDb)
    {
      _logger.LogWarning(
        "[AudioValidator:{Stage}] LEVEL: silence — L rms={LRms:F1}dB peak={LPeak:F1}dB, R rms={RRms:F1}dB peak={RPeak:F1}dB",
        stage, leftRmsDb, leftPeakDb, rightRmsDb, rightPeakDb);
      return;
    }

    _batchCount++;

    // Log periodically
    if (_batchCount % _options.LogIntervalBatches == 0)
    {
      _logger.LogInformation(
        "[AudioValidator:{Stage}] LEVEL: L rms={LRms:F1}dB peak={LPeak:F1}dB, R rms={RRms:F1}dB peak={RPeak:F1}dB (batch #{Batch})",
        stage, leftRmsDb, leftPeakDb, rightRmsDb, rightPeakDb, _batchCount);
    }
  }

  private static float ToDb(float linear)
  {
    return linear <= 0 ? float.NegativeInfinity : (float)(20.0 * Math.Log10(linear));
  }

  public void Dispose()
  {
    _cts.Cancel();
    _workAvailable.Set();
    _analysisThread.Join(TimeSpan.FromSeconds(2));
    _cts.Dispose();
    _workAvailable.Dispose();
  }

  private readonly record struct AnalysisBatch(float[] Samples, string StageName);
}
