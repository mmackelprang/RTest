using Radio.Core.Configuration;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>One field that came back from the device different from what was pushed.</summary>
/// <param name="EncoderIndex">Which encoder the mismatch is on.</param>
/// <param name="Field">The field name, for the log and the diagnostics card.</param>
/// <param name="IsSafetyField">
/// True for <c>wrap</c> and <c>reverse</c>. These decide whether the outcome is Degraded or a Hard
/// fault, which is the whole point of the tiered model: a wrong acceleration tier is a knob that
/// feels off, a wrong <c>wrap</c> on volume is a knob that can blast the room.
/// </param>
internal readonly record struct RotaryEncoderConfigMismatch(int EncoderIndex, string Field, bool IsSafetyField);

/// <summary>
/// Compares a pushed configuration against what the device reported back, and classifies the result
/// per the encoder handoff §7.6.
///
/// <para>
/// Separated from the I/O so the classification can be tested exhaustively without hardware. The
/// classification is the part that matters: it decides whether the owner sees nothing, an amber
/// badge, or a red one, and whether the host tightens its volume clamp.
/// </para>
/// </summary>
internal static class RotaryEncoderConfigVerifier
{
  /// <summary>Attempts after which a mismatch stops being ordinary and becomes reportable.</summary>
  public const int TransientAttempts = 3;

  /// <summary>Backoff between push attempts. Silent while these are in play.</summary>
  public static readonly int[] RetryBackoffMs = [250, 1000, 3000];

  /// <summary>
  /// Lists every field where <paramref name="readBack"/> disagrees with <paramref name="pushed"/>.
  ///
  /// <para>
  /// <c>min_value</c> and <c>max_value</c> are compared even though they are inert under accumulator
  /// semantics. A device the app is responsible for should not be left in an unknown state, and
  /// "inert" is an assumption about how the host reads the device rather than a guarantee about what
  /// the device does.
  /// </para>
  /// </summary>
  public static IReadOnlyList<RotaryEncoderConfigMismatch> Compare(
    RotaryEncoderDeviceConfig pushed, RotaryEncoderDeviceConfig readBack)
  {
    ArgumentNullException.ThrowIfNull(pushed);
    ArgumentNullException.ThrowIfNull(readBack);

    var mismatches = new List<RotaryEncoderConfigMismatch>();

    if (pushed.StepsPerDetent != readBack.StepsPerDetent)
    {
      mismatches.Add(new RotaryEncoderConfigMismatch(-1, "steps_per_detent", IsSafetyField: false));
    }

    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      RotaryEncoderChannelConfig a = pushed.Encoders[i];
      RotaryEncoderChannelConfig b = readBack.Encoders[i];

      Check(a.MinValue != b.MinValue, i, "min_value", safety: false);
      Check(a.MaxValue != b.MaxValue, i, "max_value", safety: false);
      Check(a.StepSize != b.StepSize, i, "step_size", safety: false);

      // The two safety fields. wrap on VOLUME is the single most safety-critical value in the
      // table; reverse on any knob means a knob that moves the wrong way, which on volume is the
      // same hazard wearing a different hat.
      Check(a.Wrap != b.Wrap, i, "wrap", safety: i == RotaryEncoderConfigDefaults.VolumeEncoderIndex);
      Check(a.Reverse != b.Reverse, i, "reverse", safety: true);

      for (int t = 0; t < RotaryEncoderDeviceConfig.TiersPerEncoder; t++)
      {
        Check(a.Tiers[t].ThresholdMs != b.Tiers[t].ThresholdMs, i, $"tier{t + 1}_threshold_ms", safety: false);
        Check(a.Tiers[t].Multiplier != b.Tiers[t].Multiplier, i, $"tier{t + 1}_multiplier", safety: false);
      }
    }

    return mismatches;

    void Check(bool differs, int index, string field, bool safety)
    {
      if (differs)
      {
        mismatches.Add(new RotaryEncoderConfigMismatch(index, field, safety));
      }
    }
  }

  /// <summary>
  /// Classifies an attempt's outcome.
  /// </summary>
  /// <param name="mismatches">Result of <see cref="Compare"/>, or null when the device did not answer.</param>
  /// <param name="attempt">1-based attempt number.</param>
  public static RotaryEncoderConfigStatus Classify(
    IReadOnlyList<RotaryEncoderConfigMismatch>? mismatches, int attempt)
  {
    if (mismatches is { Count: 0 })
    {
      return RotaryEncoderConfigStatus.Configured;
    }

    // A safety mismatch is a hard fault immediately, without waiting out the retry budget. Retrying
    // is still worth doing — the next attempt may succeed — but the host must tighten its volume
    // clamp NOW rather than three seconds from now, because the knob is live the whole time.
    if (mismatches is not null && mismatches.Any(m => m.IsSafetyField))
    {
      return RotaryEncoderConfigStatus.HardFault;
    }

    // No answer, or a feel-field mismatch, inside the retry budget. Silent: a USB peripheral missing
    // a report on the first try is ordinary.
    if (attempt < TransientAttempts)
    {
      return RotaryEncoderConfigStatus.Transient;
    }

    return RotaryEncoderConfigStatus.Degraded;
  }

  /// <summary>
  /// The host's per-event volume movement clamp for a given configuration status.
  ///
  /// <para>
  /// This is what makes the window between connect and a verified push survivable. Until read-back
  /// confirms the safety fields the device may still be on factory tiers, where one detent is worth
  /// 100 volume points, so the host refuses to act on more than a couple of units per event no
  /// matter what arrives.
  /// </para>
  /// </summary>
  public static int VolumeClampFor(RotaryEncoderConfigStatus status) =>
    status == RotaryEncoderConfigStatus.Configured
      ? RotaryEncoderConfigDefaults.VolumeClamp
      : RotaryEncoderConfigDefaults.VolumeClampUnverified;
}
