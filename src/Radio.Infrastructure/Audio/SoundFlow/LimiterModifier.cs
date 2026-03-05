using SoundFlow.Abstracts;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// A SoundFlow modifier that applies soft-knee tanh limiting to prevent
/// digital clipping. Samples below the threshold pass through unchanged;
/// above the threshold, a tanh curve compresses them toward ±1.0.
/// The transfer function is continuous at the threshold (no discontinuity).
/// </summary>
public class LimiterModifier : SoundModifier
{
  /// <summary>
  /// Default threshold: -1 dBFS ≈ 0.891 linear.
  /// Samples below this pass through unmodified.
  /// </summary>
  public const float DefaultThreshold = 0.891f;

  private readonly float _threshold;

  // Instrumentation: track limiter engagement per reporting interval
  private long _totalSamples;
  private long _limitedSamples;
  private float _maxInputAbs;
  private float _maxReduction;


  /// <summary>
  /// Initializes a new instance of the <see cref="LimiterModifier"/> class.
  /// </summary>
  /// <param name="threshold">
  /// Linear amplitude threshold (0..1) above which limiting kicks in.
  /// Defaults to <see cref="DefaultThreshold"/> (-1 dBFS).
  /// </param>
  public LimiterModifier(float threshold = DefaultThreshold)
  {
    _threshold = Math.Clamp(threshold, 0.01f, 0.999f);
    Name = "Limiter";
  }

  /// <summary>
  /// Gets a snapshot of limiter engagement stats and resets counters.
  /// Returns null if no samples have been processed since the last call.
  /// </summary>
  public LimiterStats? GetAndResetStats()
  {
    if (_totalSamples == 0) return null;

    var stats = new LimiterStats
    {
      TotalSamples = _totalSamples,
      LimitedSamples = _limitedSamples,
      EngagementPercent = _totalSamples > 0 ? (float)_limitedSamples / _totalSamples * 100f : 0f,
      MaxInputAbs = _maxInputAbs,
      MaxReductionDb = _maxReduction > 0 ? 20f * MathF.Log10(1f - _maxReduction) : 0f
    };

    _totalSamples = 0;
    _limitedSamples = 0;
    _maxInputAbs = 0;
    _maxReduction = 0;

    return stats;
  }

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    var abs = MathF.Abs(sample);
    _totalSamples++;

    // Fast path: below threshold, pass through unchanged
    if (abs <= _threshold)
      return sample;

    // Soft-knee: threshold + (1 - threshold) * tanh((|x| - threshold) / (1 - threshold))
    // This is continuous at the threshold (tanh(0) = 0) and asymptotes to ±1.0.
    var headroom = 1.0f - _threshold;
    var limited = _threshold + headroom * MathF.Tanh((abs - _threshold) / headroom);

    _limitedSamples++;
    if (abs > _maxInputAbs) _maxInputAbs = abs;
    var reduction = (abs - limited) / abs;
    if (reduction > _maxReduction) _maxReduction = reduction;

    return sample >= 0 ? limited : -limited;
  }
}

/// <summary>
/// Snapshot of limiter engagement statistics.
/// </summary>
public struct LimiterStats
{
  public long TotalSamples { get; set; }
  public long LimitedSamples { get; set; }
  public float EngagementPercent { get; set; }
  public float MaxInputAbs { get; set; }
  public float MaxReductionDb { get; set; }
}
