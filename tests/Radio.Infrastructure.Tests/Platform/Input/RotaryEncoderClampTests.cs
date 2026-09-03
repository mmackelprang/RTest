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
/// </summary>
public class RotaryEncoderClampTests
{
  [Fact]
  public void VolumeClamp_KeepsSilenceToFullAboveTheStatedFloor()
  {
    // Handoff §5.4: minimum time from silence to full must be at least 1.33 s of deliberate
    // spinning. The fastest configured tier is 6 points per 80 ms; the clamp must not permit more.
    const float hostStepPercent = 0.02f;   // RotaryEncoderOptions.VolumeStepPercent default, as a fraction

    float maxFractionPerEvent = RotaryEncoderConfigDefaults.VolumeClamp * hostStepPercent;

    Assert.True(maxFractionPerEvent <= 0.12f,
      "one event must not be able to move volume by more than a small fraction of full scale");
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
    // factory x50 deltas. This is the bound that survives that, and it is the honest one to state:
    // the clamp makes the delta irrelevant, so no single event can cross the range no matter what
    // arrives, and at least nine events are needed to get from silence to full.
    //
    // ⚠ It does NOT preserve the 1.33 s floor from handoff §5.4. That floor is a property of the
    // CONFIGURED tiers (6 points per 80 ms); a device on factory tiers can deliver events faster than
    // that, and clamping each one bounds movement per event, not events per second. The owner
    // accepted that trade in ENC-16: an acceleration tier that did not apply is a feel fault, and the
    // safety fields — wrap and reverse — are confirmed before Degraded is ever reached.
    const float hostStepPercent = 0.02f;   // RotaryEncoderOptions.VolumeStepPercent default

    float maxFractionPerEvent = RotaryEncoderConfigDefaults.VolumeClamp * hostStepPercent;

    Assert.True(maxFractionPerEvent < 1.0f,
      "one event must never cross the whole range, whatever the device believes its tiers are");
    Assert.True(Math.Ceiling(1.0 / maxFractionPerEvent) >= 9,
      "silence to full must take at least nine separate movement events");
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
  [InlineData(50, 6)]
  [InlineData(-50, -6)]
  [InlineData(int.MaxValue, 6)]
  [InlineData(int.MinValue, -6)]
  public void Clamping_IsSymmetricAndSurvivesExtremes(int delta, int expected)
  {
    // int.MinValue matters: the accumulator is differenced with unchecked arithmetic, so a wrapped
    // or corrupt sample can present as an extreme value rather than a plausible one.
    Assert.Equal(expected, Math.Clamp(delta, -RotaryEncoderConfigDefaults.VolumeClamp, RotaryEncoderConfigDefaults.VolumeClamp));
  }
}
