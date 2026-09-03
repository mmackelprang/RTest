namespace Radio.Core.Configuration;

/// <summary>
/// How far a verified configuration push got, and therefore how much the host may trust the device.
/// Mirrors the tiered fault model in the encoder handoff §7.6 — not all mismatches are equal, and
/// treating them the same is the mistake it exists to prevent.
/// </summary>
public enum RotaryEncoderConfigStatus
{
  /// <summary>No push attempted yet this connection.</summary>
  Unknown = 0,

  /// <summary>Read-back matched the pushed configuration. The device is doing what we asked.</summary>
  Configured = 1,

  /// <summary>
  /// Mismatched or unanswered within the first three attempts. Silent by design: a USB peripheral
  /// missing a report on the first try is ordinary, and reporting it would train the owner to ignore
  /// the badge.
  ///
  /// <para>
  /// The volume clamp is <b>tight</b> here, and that is not an oversight. Transient means "not
  /// confirmed <i>yet</i>", not "confirmed fine" — and this is the boot window, which is exactly when
  /// a fresh or factory-reset device is running acceleration at ×50.
  /// </para>
  /// </summary>
  Transient = 2,

  /// <summary>
  /// Read-back arrived, its <i>safety</i> fields were correct, and what is still wrong after three
  /// attempts is a <i>feel</i> field — an acceleration tier or <c>step_size</c>.
  ///
  /// <para>
  /// Knobs stay live on the <b>normal</b> host clamps, and acceleration is treated as <b>absent</b>
  /// rather than assumed present, because assuming it is how a knob ends up moving further than the
  /// host expects. The volume clamp is deliberately <i>not</i> tightened (ENC-16): a knob that feels
  /// wrong is not a knob that can blast the room, and tightening for it makes the console misreport
  /// its own safety state to the owner.
  /// </para>
  /// </summary>
  Degraded = 3,

  /// <summary>
  /// A <i>safety</i> field — <c>wrap</c> on VOLUME, or <c>reverse</c> on any knob — is not known to be
  /// right. Either read-back disagreed on one, or the device never answered within the retry budget
  /// and therefore confirmed nothing at all.
  ///
  /// <para>
  /// These are not "feels wrong", they are "can blast the room". One detent past zero on a wrapping
  /// volume knob is full scale, at 2 a.m., pointed at a sofa. The response is to tighten the host's
  /// per-event volume clamp until a verified push succeeds.
  /// </para>
  /// </summary>
  HardFault = 4,
}

/// <summary>
/// The configuration the host pushes to the encoder device, from the encoder handoff §5.2.
///
/// <para>
/// <b>Why this is a safety change, not a tuning preference.</b> A device that has never been
/// configured runs its factory defaults, and those were read off the live hardware on 2026-09-02:
/// <c>step_size = 1</c> with tiers <c>(150ms ×5), (80ms ×15), (40ms ×50)</c> on every encoder. The
/// host applies <c>VolumeStepPercent</c> per <i>device unit</i> of movement, and since ENC-20 that
/// is <b>1</b> — so a single fast detent at the ×50 tier arrives as <c>50 units = 50 points</c>,
/// half the range in one click, from a knob a guest may be touching for the first time. The host
/// clamp is what makes that window survivable: it cuts such an event to <see cref="VolumeClamp"/>,
/// <b>4 points</b>, where before ENC-20 the same clamp let through 12. The unverified window is
/// therefore strictly <i>safer</i> after this change, not merely better-feeling.
/// </para>
///
/// <para>
/// ⭐ <b>One device unit is one volume point.</b> That is the property which makes this arithmetic
/// hard to get wrong again: with <c>VolumeStepPercent = 1</c> a tier multiplier reads directly as
/// points per detent, and there is no second multiplication left to forget. ENC-20 exists because
/// there was one — <c>step_size 2</c> multiplied by 2% made a single <i>base</i> detent move volume
/// by 4 points, and every figure previously written down here counted device units as though they
/// were already points.
/// </para>
///
/// <para>
/// The values below cap volume's fastest tier at ×4, which is <b>4 volume points per detent</b>.
/// That bound is the host clamp's rather than the device's, so it holds regardless of what actually
/// arrives on the wire. Silence to full is 25 detents: at tier 2's own threshold rate (80 ms per
/// detent) <b>2.0 s</b>, and at 40 ms per detent — the fastest rate the firmware's tier ladder
/// contemplates at all — <b>1.0 s</b>.
/// </para>
///
/// <para>
/// ⚠ <b>A tier threshold is a maximum interval, not the rate the user turns at.</b> Crossing the
/// 80 ms threshold means only that detents arrived <i>at least</i> that fast; nothing stops them
/// arriving faster. A "points per 80 ms" figure is therefore not a floor and never was — the
/// wording this paragraph replaces stated exactly such a rate-based floor and could not guarantee
/// it. The honest bound is points <b>per detent</b>, which the host clamp enforces unconditionally,
/// and any figure in seconds must name the spin rate it assumes, as the two above do.
/// </para>
///
/// <para>
/// Fast enough to kill the volume when the phone rings; slow enough that no single gesture produces
/// a blast.
/// </para>
///
/// <para>
/// ⚠ <b>Do not restate that bound in revolutions.</b> It is tempting — 25 detents sounds like about
/// one turn — but <b>detents per revolution is a mechanical property of the encoder body that
/// nobody has measured yet</b>, and it is still an open question in the handoff (§7.9, §13 Q2). The
/// handoff's own feel figures say so explicitly and assume a conventional 20-detent knob purely as
/// a placeholder (§5.2), on which 25 detents is a turn and a quarter rather than a turn. Writing a
/// specific detent count here would assert a measurement that has never been taken, which is the
/// exact failure ENC-20 was raised to fix. Detents are the honest unit until the calibration flow
/// answers it.
/// </para>
/// </summary>
public static class RotaryEncoderConfigDefaults
{
  /// <summary>Encoder index of the VOLUME knob, per the physical order VOLUME · SOURCE · PRESETS · TUNING.</summary>
  public const int VolumeEncoderIndex = 0;

  /// <summary>
  /// Host per-event movement clamp for volume, in device units — which, since
  /// <c>VolumeStepPercent</c> is 1, is also <b>volume points</b>. The two are one quantity here, so
  /// this number can be read either way without converting between them.
  ///
  /// <para>
  /// It equals the fastest configured tier's movement (<c>step_size 1</c> × tier 2's ×4), and that
  /// equality is the design rather than a coincidence: the clamp permits <i>exactly</i> the
  /// configured top tier and nothing beyond it. A device doing what it was told is never clamped at
  /// all; a device on factory tiers is cut back to the same 4 points the top tier would have
  /// produced.
  /// </para>
  /// </summary>
  public const int VolumeClamp = 4;

  /// <summary>
  /// Host per-event clamp for the selector knobs — SOURCE and PRESETS.
  ///
  /// <para>
  /// One detent, one entry, always. A seven-entry list has no long traversal to make bearable, so
  /// acceleration on it only means a quick flick lands somewhere the user did not aim.
  /// </para>
  /// </summary>
  public const int SelectorClamp = 1;

  /// <summary>
  /// Host per-event clamp for tuning against a radio source.
  ///
  /// <para>
  /// This one is not only about feel. The tuner is stepped by <b>awaiting one hardware call per
  /// step</b>, so an unclamped delta of 50 from a factory acceleration tier becomes fifty sequential
  /// tuner calls from a single detent — a load spike on a box where incidental load correlates with
  /// audible distortion.
  /// </para>
  /// </summary>
  public const int TuningClamp = 8;

  /// <summary>
  /// Tightened volume clamp used while a safety field is unverified.
  ///
  /// <para>
  /// This is what makes the window between connect and a verified push survivable: until read-back
  /// confirms <c>wrap</c> and <c>reverse</c>, the device may still be on factory tiers, and the host
  /// refuses to act on more than this much movement per event regardless of what arrives.
  /// </para>
  ///
  /// <para>
  /// The trigger is the <i>safety</i> fields specifically, not "anything that failed to apply".
  /// A <see cref="RotaryEncoderConfigStatus.Degraded"/> console — read-back arrived, <c>wrap</c> and
  /// <c>reverse</c> were right in it, an acceleration tier was not — runs on
  /// <see cref="VolumeClamp"/>. See <c>RotaryEncoderConfigVerifier.VolumeClampFor</c> for the table.
  /// </para>
  ///
  /// <para>
  /// <b>ENC-20 left the number at 2 and thereby tightened it.</b> It used to bound 2 units = 4
  /// points; it now bounds 2 units = <b>2 points</b>, because <c>VolumeStepPercent</c> fell from 2
  /// to 1. Strictly tighter in absolute terms, while still being twice the base rate — so the knob
  /// is sluggish during the boot window rather than dead. That distinction earns its keep: a knob
  /// that appears not to work is a knob the user keeps turning, which is the input pattern this
  /// whole mechanism exists to survive.
  /// </para>
  ///
  /// <para>
  /// ⚠ Do <b>not</b> raise this to 4 to "match" the new <see cref="VolumeClamp"/>. That would make
  /// the unverified clamp equal to the normal one and destroy the tightening outright — the
  /// distinction between "confirmed safe" and "not confirmed yet" would still be classified,
  /// reported and tested, and would no longer do anything.
  /// </para>
  /// </summary>
  public const int VolumeClampUnverified = 2;

  /// <summary>
  /// Builds the §5.2 configuration.
  ///
  /// <para>
  /// <c>min_value</c>, <c>max_value</c> and <c>wrap</c> are inert under accumulator semantics — the
  /// host differences movement and owns the range itself. They are pushed anyway: a field being
  /// unused today is not a reason to leave a device the app is responsible for in an unknown state,
  /// and <c>wrap</c> in particular is verified as a safety field precisely because its being inert
  /// is an assumption rather than a guarantee.
  /// </para>
  /// </summary>
  public static RotaryEncoderDeviceConfig Create()
  {
    var config = new RotaryEncoderDeviceConfig
    {
      Version = RotaryEncoderDeviceConfig.SupportedVersion,
      StepsPerDetent = 4,
    };

    // Enc 0 — VOLUME. step_size 1, so with VolumeStepPercent = 1 one detent is one volume point and
    // a tier multiplier is literally points per detent (ENC-20). Two tiers, and the third is still
    // deliberately absent — see the slew argument above: ×4 at 40 ms per detent already crosses the
    // range in a second, so a further rung would put a blast inside one flick of the wrist.
    config.Encoders[0] = Channel(max: 100, step: 1,
      t1: (150, 2), t2: (80, 4), t3: (0, 0));

    // Enc 1 — SOURCE. Acceleration disabled outright. A seven-entry list with a x5 multiplier means
    // one quick flick moves the highlight five entries and lands somewhere the user did not aim.
    config.Encoders[1] = Channel(max: 6, step: 1,
      t1: (0, 0), t2: (0, 0), t3: (0, 0));

    // Enc 2 — PRESETS. Same reasoning, and it keeps the two adjacent selector knobs interchangeable
    // in the hand.
    config.Encoders[2] = Channel(max: 6, step: 1,
      t1: (0, 0), t2: (0, 0), t3: (0, 0));

    // Enc 3 — TUNING. Acceleration earns its place here: 99 steps end to end across the FM grid,
    // which is also why max_value is 99 rather than the handoff table's 0.
    //
    // ⚠ The table marks max_value "inert", and it is — under accumulator semantics the host
    // differences movement and owns the range. But INERT TO THE HOST IS NOT IGNORED BY THE DEVICE:
    // the firmware validates `min_value >= max_value` and rejects the config, and validation is
    // all-or-nothing across all four encoders. A 0 here silently discarded the entire push,
    // including the volume tiers that are the whole point of this row. Any positive value would
    // satisfy the device; 99 is used because it is the real span of the FM channel grid, so the
    // number means something to the next reader instead of being an arbitrary placeholder.
    config.Encoders[3] = Channel(max: 99, step: 1,
      t1: (150, 2), t2: (80, 4), t3: (40, 8));

    return config;
  }

  private static RotaryEncoderChannelConfig Channel(
    int max, int step, (ushort ms, ushort mult) t1, (ushort ms, ushort mult) t2, (ushort ms, ushort mult) t3) =>
    new()
    {
      MinValue = 0,
      MaxValue = max,
      StepSize = step,
      // false on every knob. One detent past zero on a wrapping volume knob is full scale.
      Wrap = false,
      // false on every knob, meaning clockwise increases. If a knob is wired backwards this flag is
      // the fix, and it is the one field a human should ever edit.
      Reverse = false,
      Tiers =
      [
        new RotaryEncoderAccelerationTier { ThresholdMs = t1.ms, Multiplier = t1.mult },
        new RotaryEncoderAccelerationTier { ThresholdMs = t2.ms, Multiplier = t2.mult },
        new RotaryEncoderAccelerationTier { ThresholdMs = t3.ms, Multiplier = t3.mult },
      ],
    };
}
