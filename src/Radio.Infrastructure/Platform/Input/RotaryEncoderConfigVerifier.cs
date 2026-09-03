using Radio.Core.Configuration;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>One field that came back from the device different from what was pushed.</summary>
/// <param name="EncoderIndex">Which encoder the mismatch is on.</param>
/// <param name="Field">The field name, for the log and the diagnostics card.</param>
/// <param name="IsSafetyField">
/// True for <c>wrap</c> and <c>reverse</c>. These decide whether the outcome is Degraded or a Hard
/// fault, and therefore whether the host tightens its volume clamp — which is the whole point of the
/// tiered model: a wrong acceleration tier is a knob that feels off, a wrong <c>wrap</c> on volume is
/// a knob that can blast the room.
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
  /// <remarks>
  /// ⚠ <b>The tier a state lands in is the sole input to <see cref="VolumeClampFor"/></b>, so the
  /// boundaries here are a safety decision and not only a reporting one. In particular
  /// <see cref="RotaryEncoderConfigStatus.Degraded"/> means <i>read-back arrived and the safety
  /// fields in it were correct</i> — nothing that leaves <c>wrap</c> and <c>reverse</c> unconfirmed
  /// may be classified there, because Degraded runs on the normal clamp (ENC-16).
  /// </remarks>
  public static RotaryEncoderConfigStatus Classify(
    IReadOnlyList<RotaryEncoderConfigMismatch>? mismatches, int attempt)
  {
    // The device never answered. Nothing about it is confirmed — least of all wrap and reverse — so
    // this cannot settle in Degraded, whose whole licence to run the normal clamp is that read-back
    // arrived and the safety fields in it were right.
    //
    // Inside the retry budget that is Transient and silent; once the budget is spent it is a hard
    // fault, because "we cannot confirm the safety fields" and "the safety fields came back wrong"
    // have exactly the same consequence for a live knob: the host must not trust the device's
    // acceleration, and the owner must be told the volume knob is limited.
    if (mismatches is null)
    {
      return attempt < TransientAttempts
        ? RotaryEncoderConfigStatus.Transient
        : RotaryEncoderConfigStatus.HardFault;
    }

    if (mismatches.Count == 0)
    {
      return RotaryEncoderConfigStatus.Configured;
    }

    // A safety mismatch is a hard fault immediately, without waiting out the retry budget. Retrying
    // is still worth doing — the next attempt may succeed — but the host must tighten its volume
    // clamp NOW rather than three seconds from now, because the knob is live the whole time.
    if (mismatches.Any(m => m.IsSafetyField))
    {
      return RotaryEncoderConfigStatus.HardFault;
    }

    // A feel-field mismatch inside the retry budget. Silent: a USB peripheral missing a report on
    // the first try is ordinary.
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
  /// The clamp answers one question — <b>are this device's safety fields confirmed?</b> — and not
  /// "did everything apply". <c>wrap</c> on VOLUME and <c>reverse</c> on any knob are what decide
  /// whether a knob can blast the room; an acceleration tier that did not apply decides only how the
  /// knob feels, and tightening the volume clamp for it buys nothing while telling the owner
  /// something untrue about the console's safety state (ENC-16).
  /// </para>
  ///
  /// <para>
  /// So the tightened clamp covers exactly the tiers where a safety field is <i>unverified or
  /// disagreeing</i> — <see cref="RotaryEncoderConfigStatus.Unknown"/> (no push attempted yet),
  /// <see cref="RotaryEncoderConfigStatus.Transient"/> (not confirmed <i>yet</i>; the boot window is
  /// exactly when a fresh or factory-reset Pico is running acceleration at ×50) and
  /// <see cref="RotaryEncoderConfigStatus.HardFault"/> (a safety field came back wrong, or never came
  /// back at all). <see cref="RotaryEncoderConfigStatus.Degraded"/> keeps the normal clamp: read-back
  /// arrived and <c>wrap</c> and <c>reverse</c> were correct in it.
  /// </para>
  ///
  /// <para>
  /// ⚠ This method and <c>EncoderFaultRules.NotificationCopy</c> must stay in agreement: the owner is
  /// told "Volume is limited until this is fixed" on a hard fault and told nothing about volume on a
  /// Degraded one, and both statements are only true while this table is what it is.
  /// </para>
  /// </summary>
  public static int VolumeClampFor(RotaryEncoderConfigStatus status) =>
    status is RotaryEncoderConfigStatus.Configured or RotaryEncoderConfigStatus.Degraded
      ? RotaryEncoderConfigDefaults.VolumeClamp
      : RotaryEncoderConfigDefaults.VolumeClampUnverified;
}
