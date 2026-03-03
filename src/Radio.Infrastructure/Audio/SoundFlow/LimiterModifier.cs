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

  /// <inheritdoc/>
  public override float ProcessSample(float sample, int channel)
  {
    var abs = MathF.Abs(sample);

    // Fast path: below threshold, pass through unchanged
    if (abs <= _threshold)
      return sample;

    // Soft-knee: threshold + (1 - threshold) * tanh((|x| - threshold) / (1 - threshold))
    // This is continuous at the threshold (tanh(0) = 0) and asymptotes to ±1.0.
    var headroom = 1.0f - _threshold;
    var limited = _threshold + headroom * MathF.Tanh((abs - _threshold) / headroom);

    return sample >= 0 ? limited : -limited;
  }
}
