using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Audio.Validation;

/// <summary>
/// Validates audio using Goertzel algorithm to detect expected frequencies (200Hz L, 300Hz R).
/// Detects silence, wrong frequencies, and channel leakage.
/// Analysis runs on a background thread — Submit never blocks the audio thread.
/// </summary>
public sealed class FrequencyValidator : IAudioValidator, IDisposable
{
  private readonly ILogger<FrequencyValidator> _logger;
  private readonly AudioValidationOptions _options;
  private readonly ConcurrentQueue<AnalysisBatch> _queue = new();
  private readonly Thread _analysisThread;
  private readonly CancellationTokenSource _cts = new();
  private readonly ManualResetEventSlim _workAvailable = new(false);
  private readonly int _sampleRate;
  private readonly int _channels;
  private int _batchCount;

  private const int ExpectedLeftHz = 200;
  private const int ExpectedRightHz = 300;

  public FrequencyValidator(
    ILogger<FrequencyValidator> logger,
    AudioValidationOptions options,
    int sampleRate = 48000,
    int channels = 2)
  {
    _logger = logger;
    _options = options;
    _sampleRate = sampleRate;
    _channels = channels;

    _analysisThread = new Thread(AnalysisLoop)
    {
      Name = "AudioValidator-Frequency",
      IsBackground = true,
      Priority = ThreadPriority.BelowNormal
    };
    _analysisThread.Start();
  }

  /// <inheritdoc/>
  public void Submit(ReadOnlySpan<float> samples, string stageName)
  {
    // Copy samples off audio thread immediately
    var copy = samples.ToArray();
    _queue.Enqueue(new AnalysisBatch(copy, stageName));
    _workAvailable.Set();
  }

  /// <inheritdoc/>
  public Task FlushAsync(CancellationToken cancellationToken = default)
  {
    _cts.Cancel();
    _workAvailable.Set();

    // Process remaining items synchronously
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

    if (_channels < 2 || samples.Length < _channels * 2)
      return;

    var frames = samples.Length / _channels;

    // Deinterleave channels
    var left = new float[frames];
    var right = new float[frames];
    for (var i = 0; i < frames; i++)
    {
      left[i] = samples[i * _channels];
      right[i] = samples[i * _channels + 1];
    }

    // RMS levels
    var leftRms = CalculateRmsDb(left);
    var rightRms = CalculateRmsDb(right);

    // Silence check
    if (leftRms < _options.SilenceThresholdDb && rightRms < _options.SilenceThresholdDb)
    {
      _logger.LogWarning("[AudioValidator:{Stage}] SILENCE detected — L:{LeftDb:F1}dB R:{RightDb:F1}dB",
        stage, leftRms, rightRms);
      return;
    }

    // Goertzel frequency detection
    var left200 = GoertzelMagnitude(left, ExpectedLeftHz, _sampleRate);
    var left300 = GoertzelMagnitude(left, ExpectedRightHz, _sampleRate);
    var right200 = GoertzelMagnitude(right, ExpectedLeftHz, _sampleRate);
    var right300 = GoertzelMagnitude(right, ExpectedRightHz, _sampleRate);

    var threshold = _options.FrequencyDetectionThreshold;
    var hasIssue = false;

    // Check expected frequencies present
    if (left200 < threshold)
    {
      _logger.LogWarning("[AudioValidator:{Stage}] LEFT missing 200Hz (mag={Mag:F4})",
        stage, left200);
      hasIssue = true;
    }

    if (right300 < threshold)
    {
      _logger.LogWarning("[AudioValidator:{Stage}] RIGHT missing 300Hz (mag={Mag:F4})",
        stage, right300);
      hasIssue = true;
    }

    // Check for channel leakage (wrong frequency in wrong channel)
    if (left300 > threshold * 0.5f && left300 > left200 * 0.3f)
    {
      _logger.LogWarning("[AudioValidator:{Stage}] LEFT channel leakage: 300Hz={Leak:F4} vs 200Hz={Expected:F4}",
        stage, left300, left200);
      hasIssue = true;
    }

    if (right200 > threshold * 0.5f && right200 > right300 * 0.3f)
    {
      _logger.LogWarning("[AudioValidator:{Stage}] RIGHT channel leakage: 200Hz={Leak:F4} vs 300Hz={Expected:F4}",
        stage, right200, right300);
      hasIssue = true;
    }

    _batchCount++;

    // Log OK periodically
    if (!hasIssue && _batchCount % _options.LogIntervalBatches == 0)
    {
      _logger.LogInformation(
        "[AudioValidator:{Stage}] OK — L:200Hz={L200:F3} R:300Hz={R300:F3} | L:{LDb:F1}dB R:{RDb:F1}dB (batch #{Batch})",
        stage, left200, right300, leftRms, rightRms, _batchCount);
    }
  }

  /// <summary>
  /// Goertzel algorithm — O(N) single-frequency magnitude detection.
  /// Returns normalized magnitude (0.0 to ~1.0 for full-scale sine).
  /// </summary>
  internal static float GoertzelMagnitude(ReadOnlySpan<float> samples, float targetHz, int sampleRate)
  {
    var n = samples.Length;
    if (n == 0) return 0f;

    var k = (int)(0.5 + n * targetHz / sampleRate);
    var w = 2.0 * Math.PI * k / n;
    var coeff = 2.0 * Math.Cos(w);

    double s0 = 0, s1 = 0, s2 = 0;
    for (var i = 0; i < n; i++)
    {
      s0 = samples[i] + coeff * s1 - s2;
      s2 = s1;
      s1 = s0;
    }

    var power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
    // Normalize by N/2 (amplitude of a full-scale sine in DFT)
    var magnitude = Math.Sqrt(Math.Max(0, power)) / (n / 2.0);
    return (float)magnitude;
  }

  private static float CalculateRmsDb(ReadOnlySpan<float> samples)
  {
    if (samples.Length == 0) return float.NegativeInfinity;

    var sum = 0.0;
    for (var i = 0; i < samples.Length; i++)
      sum += samples[i] * (double)samples[i];

    var rms = Math.Sqrt(sum / samples.Length);
    return rms <= 0 ? float.NegativeInfinity : (float)(20.0 * Math.Log10(rms));
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
