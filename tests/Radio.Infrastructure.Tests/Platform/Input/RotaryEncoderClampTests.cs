using Radio.Core.Configuration;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Pins the ENC-3 host clamp values.
///
/// <para>
/// These are applied <b>unconditionally</b>, not as a fallback, and that is the whole point. There is
/// a real window on every boot and after every reconnect during which the device runs whatever is in
/// its flash — on a fresh or reset Pico, factory defaults including volume acceleration at ×50 — and
/// the knobs are live throughout it. The clamps are what make that window sluggish rather than
/// dangerous, so their values are worth pinning rather than trusting to review.
/// </para>
///
/// <para>
/// ⚠ <b>The host step is read from <see cref="RotaryEncoderOptions"/> rather than written down here.</b>
/// It used to be a local <c>const 0.02f</c> with a comment claiming it was that option's default —
/// which is exactly the failure ENC-20 was raised for. A duplicated constant is true only until
/// somebody changes the original, and a comment asserting it still matches survives the change
/// silently. Deriving it means these tests move when the option moves, or fail loudly if the
/// relationship they assert has stopped holding.
/// </para>
/// </summary>
public class RotaryEncoderClampTests
{
  /// <summary>
  /// Volume points per device unit of movement, as a fraction of full scale — taken from the live
  /// option default, never restated.
  /// </summary>
  private static float HostStepFraction => new RotaryEncoderOptions().VolumeStepPercent / 100f;

  [Fact]
  public void OneDetentAtBaseSpeed_MovesVolumeByExactlyOnePoint()
  {
    // ENC-20, and the regression the owner actually reported: below every acceleration threshold —
    // an ordinary, unhurried turn — one detent moved volume by FOUR points, because the VOLUME
    // channel carried step_size 2 and the host multiplied the resulting device units by 2% again.
    //
    // This is the relationship no other test in this file pinned, and the one that makes all the
    // rest legible: with step_size 1 and VolumeStepPercent 1, ONE DEVICE UNIT IS ONE VOLUME POINT,
    // so a tier multiplier reads directly as points per detent with no second multiplication left
    // to forget.
    var volume = RotaryEncoderConfigDefaults.Create().Encoders[RotaryEncoderConfigDefaults.VolumeEncoderIndex];

    Assert.Equal(1, volume.StepSize * new RotaryEncoderOptions().VolumeStepPercent);
  }

  [Fact]
  public void VolumeClamp_EqualsTheFastestConfiguredTiersMovement()
  {
    // The clamp is not an arbitrary ceiling picked to be "safe enough" — it is exactly what the top
    // configured tier produces, so it permits the configured device in full and nothing beyond it.
    // Pinned as an equality rather than an inequality precisely so the two cannot drift apart in
    // silence: raising a tier without raising the clamp would quietly cap the feel that was just
    // configured, and raising the clamp without a tier would licence movement nothing is meant to
    // send.
    var volume = RotaryEncoderConfigDefaults.Create().Encoders[RotaryEncoderConfigDefaults.VolumeEncoderIndex];
    int fastestTierMultiplier = volume.Tiers.Max(t => (int)t.Multiplier);

    Assert.Equal(RotaryEncoderConfigDefaults.VolumeClamp, volume.StepSize * fastestTierMultiplier);
  }

  [Fact]
  public void VolumeClamp_KeepsSilenceToFullAboveTheStatedFloor()
  {
    // The bound that is honestly enforceable is POINTS PER DETENT, not points per unit of time.
    // The fastest configured tier is ×4 on step_size 1, so 4 volume points per detent, and the
    // clamp must not permit more than the tier it was sized against.
    //
    // ⚠ Deliberately NOT phrased as "6 points per 80 ms", which is what this test used to claim.
    // That figure was wrong twice over: it read device units as volume points, and it treated a
    // tier threshold as the rate the user turns at. A threshold is a MAXIMUM INTERVAL — crossing it
    // means detents arrived at least that fast, and nothing stops them arriving faster — so no
    // rate-based floor follows from it. Silence to full is 25 detents: 2.0 s at 80 ms per detent,
    // 1.0 s at 40 ms, and each of those numbers has to name its spin rate to mean anything.
    float maxFractionPerEvent = RotaryEncoderConfigDefaults.VolumeClamp * HostStepFraction;

    Assert.True(maxFractionPerEvent <= 0.04f + 1e-6f,
      "one event must not move volume by more than the fastest configured tier's 4 points");
    Assert.True(maxFractionPerEvent < 1.0f, "one event must never cross the whole range");
  }

  [Fact]
  public void VolumeClamp_IsTighterBeforeAVerifiedPush()
  {
    // The unverified clamp covers the window where the device may still be on factory tiers.
    Assert.True(
      RotaryEncoderConfigDefaults.VolumeClampUnverified < RotaryEncoderConfigDefaults.VolumeClamp);
  }

  [Fact]
  public void VolumeClamp_BoundsASingleEventEvenOnFactoryTiers()
  {
    // ENC-16 relaxed the tightened clamp for Degraded, so a Degraded console runs on VolumeClamp
    // while its acceleration tiers are, by definition, NOT confirmed — the device may be emitting
    // factory ×50 deltas. This is the bound that survives that, and it is the honest one to state:
    // the clamp makes the delta irrelevant, so no single event can cross the range no matter what
    // arrives, and at least 25 events are needed to get from silence to full.
    //
    // ⚠ What it does NOT bound is elapsed time. Clamping bounds movement PER EVENT, not events per
    // second — a device on factory tiers can deliver events as fast as it likes, and 25 of them can
    // arrive well inside a second. That point was correct in the version of this comment ENC-20
    // replaced and is kept; only the arithmetic around it was wrong, since it cited "the 1.33 s
    // floor from handoff §5.4" — a figure that both miscounted device units as volume points and
    // assumed a spin rate it could not enforce. The owner accepted the trade in ENC-16: an
    // acceleration tier that did not apply is a feel fault, and the safety fields — wrap and
    // reverse — are confirmed before Degraded is ever reached.
    float maxFractionPerEvent = RotaryEncoderConfigDefaults.VolumeClamp * HostStepFraction;

    Assert.True(maxFractionPerEvent < 1.0f,
      "one event must never cross the whole range, whatever the device believes its tiers are");
    Assert.True(Math.Ceiling(1.0 / maxFractionPerEvent) >= 25,
      "silence to full must take at least twenty-five separate movement events");
  }

  [Fact]
  public void SelectorClamp_IsExactlyOneEntry()
  {
    // One detent, one entry, always — on SOURCE and PRESETS alike. A seven-entry list has no long
    // traversal to make bearable, so any multiplier only means landing somewhere nobody aimed.
    Assert.Equal(1, RotaryEncoderConfigDefaults.SelectorClamp);
  }

  [Fact]
  public void TuningClamp_BoundsTheNumberOfHardwareCallsPerDetent()
  {
    // Not only a feel guard. The tuner is stepped by awaiting one hardware call per step, so the
    // clamp is also the bound on how many sequential tuner calls a single detent can trigger.
    Assert.Equal(8, RotaryEncoderConfigDefaults.TuningClamp);
    Assert.True(RotaryEncoderConfigDefaults.TuningClamp < 50,
      "an unclamped factory x50 delta would become fifty sequential tuner calls from one detent");
  }

  [Theory]
  [InlineData(0, 0)]
  [InlineData(3, 3)]
  [InlineData(-3, -3)]
  [InlineData(50, RotaryEncoderConfigDefaults.VolumeClamp)]
  [InlineData(-50, -RotaryEncoderConfigDefaults.VolumeClamp)]
  [InlineData(int.MaxValue, RotaryEncoderConfigDefaults.VolumeClamp)]
  [InlineData(int.MinValue, -RotaryEncoderConfigDefaults.VolumeClamp)]
  public void Clamping_IsSymmetricAndSurvivesExtremes(int delta, int expected)
  {
    // int.MinValue matters: the accumulator is differenced with unchecked arithmetic, so a wrapped
    // or corrupt sample can present as an extreme value rather than a plausible one.
    //
    // The saturating cases name the constant rather than its current value, for the same reason
    // HostStepFraction is derived: a literal here would have to be re-edited by hand every time the
    // clamp moves, and would keep passing against a stale expectation if it were not.
    Assert.Equal(expected, Math.Clamp(delta, -RotaryEncoderConfigDefaults.VolumeClamp, RotaryEncoderConfigDefaults.VolumeClamp));
  }
}
