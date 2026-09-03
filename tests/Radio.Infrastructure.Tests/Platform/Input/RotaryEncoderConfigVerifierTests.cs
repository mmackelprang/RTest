using Radio.Core.Configuration;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Tests for the ENC-11 configuration verification and its tiered fault model (handoff §7.6).
///
/// <para>
/// The classification is the part worth testing exhaustively. It decides whether the owner sees
/// nothing, an amber badge or a red one, and whether the host tightens its volume clamp — and the
/// distinction it draws is the whole point: a wrong acceleration tier is a knob that feels off, a
/// wrong <c>wrap</c> on volume is a knob that can blast the room.
/// </para>
/// </summary>
public class RotaryEncoderConfigVerifierTests
{
  private static RotaryEncoderDeviceConfig Desired() => RotaryEncoderConfigDefaults.Create();

  private static RotaryEncoderDeviceConfig Clone(RotaryEncoderDeviceConfig c)
  {
    byte[] wire = RotaryEncoderConfigCodec.Encode(c);
    RotaryEncoderConfigCodec.TryDecode(wire, wire.Length, out var copy);
    return copy;
  }

  [Fact]
  public void Compare_IdenticalConfigs_ReportsNoMismatches()
  {
    Assert.Empty(RotaryEncoderConfigVerifier.Compare(Desired(), Clone(Desired())));
  }

  [Fact]
  public void Defaults_TameTheFactoryVolumeAcceleration()
  {
    // The measured factory state on this hardware was step_size 1 with tiers x5 / x15 / x50. At the
    // host's 2% per unit that is 100 volume points in one detent. These defaults exist to replace
    // exactly that, so the values are worth pinning rather than trusting.
    RotaryEncoderChannelConfig volume = Desired().Encoders[RotaryEncoderConfigDefaults.VolumeEncoderIndex];

    Assert.Equal(2, volume.StepSize);
    Assert.Equal(2, volume.Tiers[0].Multiplier);
    Assert.Equal(3, volume.Tiers[1].Multiplier);
    Assert.Equal(0, volume.Tiers[2].ThresholdMs);   // third tier disabled outright
    Assert.Equal(0, volume.Tiers[2].Multiplier);

    int worstCasePoints = volume.StepSize * volume.Tiers[1].Multiplier;
    Assert.Equal(6, worstCasePoints);
    Assert.True(worstCasePoints * 2 < 100, "one detent must not be able to cross the volume range");
  }

  [Fact]
  public void Defaults_DisableAccelerationOnTheSelectorKnobs()
  {
    // A seven-entry list with a multiplier means one quick flick lands somewhere the user did not
    // aim. One detent, one entry, always — on SOURCE and PRESETS alike.
    RotaryEncoderDeviceConfig c = Desired();

    foreach (int i in new[] { 1, 2 })
    {
      Assert.All(c.Encoders[i].Tiers, t => Assert.Equal(0, t.ThresholdMs));
      Assert.All(c.Encoders[i].Tiers, t => Assert.Equal(0, t.Multiplier));
    }
  }

  [Fact]
  public void Defaults_NeverWrapAndNeverReverse()
  {
    // wrap = false on volume is the single most safety-critical value in the table: one detent past
    // zero would be full scale.
    RotaryEncoderDeviceConfig c = Desired();

    Assert.All(c.Encoders, e => Assert.False(e.Wrap));
    Assert.All(c.Encoders, e => Assert.False(e.Reverse));
  }

  [Fact]
  public void Compare_WrapMismatchOnVolume_IsASafetyField()
  {
    RotaryEncoderDeviceConfig readBack = Clone(Desired());
    readBack.Encoders[RotaryEncoderConfigDefaults.VolumeEncoderIndex].Wrap = true;

    var mismatches = RotaryEncoderConfigVerifier.Compare(Desired(), readBack);

    Assert.Contains(mismatches, m => m.Field == "wrap" && m.IsSafetyField);
  }

  [Fact]
  public void Compare_ReverseMismatch_IsASafetyFieldOnEveryKnob()
  {
    // A knob that moves the wrong way is the same hazard wearing a different hat.
    for (int i = 0; i < RotaryEncoderDeviceConfig.EncoderCount; i++)
    {
      RotaryEncoderDeviceConfig readBack = Clone(Desired());
      readBack.Encoders[i].Reverse = true;

      var mismatches = RotaryEncoderConfigVerifier.Compare(Desired(), readBack);

      Assert.Contains(mismatches, m => m.EncoderIndex == i && m.Field == "reverse" && m.IsSafetyField);
    }
  }

  [Fact]
  public void Compare_AccelerationMismatch_IsNotASafetyField()
  {
    RotaryEncoderDeviceConfig readBack = Clone(Desired());
    readBack.Encoders[0].Tiers[0].Multiplier = 50;

    var mismatches = RotaryEncoderConfigVerifier.Compare(Desired(), readBack);

    Assert.NotEmpty(mismatches);
    Assert.All(mismatches, m => Assert.False(m.IsSafetyField));
  }

  [Fact]
  public void Classify_MatchingReadBack_IsConfigured()
  {
    Assert.Equal(
      RotaryEncoderConfigStatus.Configured,
      RotaryEncoderConfigVerifier.Classify(Array.Empty<RotaryEncoderConfigMismatch>(), attempt: 1));
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  public void Classify_FeelFieldMismatchInsideTheRetryBudget_IsTransientAndSilent(int attempt)
  {
    var mismatches = new[] { new RotaryEncoderConfigMismatch(0, "tier1_multiplier", IsSafetyField: false) };

    Assert.Equal(
      RotaryEncoderConfigStatus.Transient,
      RotaryEncoderConfigVerifier.Classify(mismatches, attempt));
  }

  [Fact]
  public void Classify_FeelFieldStillWrongAfterTheBudget_IsDegraded()
  {
    var mismatches = new[] { new RotaryEncoderConfigMismatch(0, "step_size", IsSafetyField: false) };

    Assert.Equal(
      RotaryEncoderConfigStatus.Degraded,
      RotaryEncoderConfigVerifier.Classify(mismatches, RotaryEncoderConfigVerifier.TransientAttempts));
  }

  [Fact]
  public void Classify_SafetyFieldMismatch_IsAHardFaultImmediately()
  {
    // Deliberately not held back until the retry budget is spent. Retrying is still worth doing, but
    // the knob is live the whole time, so the host must tighten its clamp now rather than three
    // seconds from now.
    var mismatches = new[] { new RotaryEncoderConfigMismatch(0, "wrap", IsSafetyField: true) };

    Assert.Equal(
      RotaryEncoderConfigStatus.HardFault,
      RotaryEncoderConfigVerifier.Classify(mismatches, attempt: 1));
  }

  [Fact]
  public void Classify_NoResponse_IsSilentInsideTheBudget()
  {
    // "The device did not confirm" is not an error to swallow — it is a mismatch that has not run out
    // of retries yet. A USB peripheral missing a report on the first try is ordinary.
    Assert.Equal(RotaryEncoderConfigStatus.Transient, RotaryEncoderConfigVerifier.Classify(null, 1));
  }

  [Fact]
  public void Classify_NoResponseAfterTheBudget_IsAHardFaultNotDegraded()
  {
    // ENC-16. This is the case that decides whether relaxing Degraded's clamp is safe. A device that
    // never answered has confirmed NOTHING — least of all wrap and reverse — so it must not land in
    // the one non-Configured tier that runs the normal volume clamp. It may still be on factory
    // tiers, which is the "one detent from silence to full" hazard the whole arc exists to prevent.
    Assert.Equal(
      RotaryEncoderConfigStatus.HardFault,
      RotaryEncoderConfigVerifier.Classify(null, RotaryEncoderConfigVerifier.TransientAttempts));

    // And the consequence, asserted rather than assumed: the clamp is the tight one either way.
    Assert.Equal(
      RotaryEncoderConfigDefaults.VolumeClampUnverified,
      RotaryEncoderConfigVerifier.VolumeClampFor(
        RotaryEncoderConfigVerifier.Classify(null, RotaryEncoderConfigVerifier.TransientAttempts)));
  }

  [Theory]
  [InlineData(RotaryEncoderConfigStatus.Unknown)]
  [InlineData(RotaryEncoderConfigStatus.Transient)]
  [InlineData(RotaryEncoderConfigStatus.HardFault)]
  public void VolumeClamp_IsTightenedWhereverASafetyFieldIsUnconfirmed(RotaryEncoderConfigStatus status)
  {
    // ENC-16: the predicate is "are wrap and reverse confirmed", not "did everything apply".
    //
    // Unknown  — no push attempted yet this connection.
    // Transient — not confirmed YET. This is the boot window, which is exactly when a fresh or
    //             factory-reset Pico is running acceleration at x50, so it is the single most
    //             important row in this table to get right.
    // HardFault — a safety field read back wrong, or the device never answered at all.
    Assert.Equal(
      RotaryEncoderConfigDefaults.VolumeClampUnverified,
      RotaryEncoderConfigVerifier.VolumeClampFor(status));
  }

  [Theory]
  [InlineData(RotaryEncoderConfigStatus.Configured)]
  [InlineData(RotaryEncoderConfigStatus.Degraded)]
  public void VolumeClamp_StaysNormalOnceTheSafetyFieldsAreConfirmed(RotaryEncoderConfigStatus status)
  {
    // ENC-16. Degraded means read-back arrived and wrap/reverse were right in it; only a feel field
    // (an acceleration tier, step_size) disagreed. That is a knob that feels wrong, not a knob that
    // can blast the room — and tightening the clamp for it made the console misreport its own safety
    // state, since ENC-12's Degraded toast tells the owner only that the knobs "may feel wrong".
    Assert.Equal(
      RotaryEncoderConfigDefaults.VolumeClamp,
      RotaryEncoderConfigVerifier.VolumeClampFor(status));
  }

  [Fact]
  public void VolumeClamp_FollowsTheSafetyFieldRatherThanTheMismatchCount()
  {
    // The whole table in one assertion, driven off Classify rather off hand-written statuses, so a
    // future change to the tier boundaries cannot quietly relax the clamp for an unverified device.
    var feel = new[] { new RotaryEncoderConfigMismatch(0, "tier1_multiplier", IsSafetyField: false) };
    var safety = new[] { new RotaryEncoderConfigMismatch(0, "reverse", IsSafetyField: true) };
    int budget = RotaryEncoderConfigVerifier.TransientAttempts;

    Assert.Equal(
      RotaryEncoderConfigDefaults.VolumeClamp,
      RotaryEncoderConfigVerifier.VolumeClampFor(RotaryEncoderConfigVerifier.Classify(feel, budget)));

    Assert.Equal(
      RotaryEncoderConfigDefaults.VolumeClampUnverified,
      RotaryEncoderConfigVerifier.VolumeClampFor(RotaryEncoderConfigVerifier.Classify(safety, 1)));

    // Sixteen feel-field mismatches are still only feel fields. Volume of disagreement is not the
    // predicate; which fields disagreed is.
    var manyFeel = Enumerable.Range(0, 16)
      .Select(i => new RotaryEncoderConfigMismatch(i % 4, $"tier{(i % 3) + 1}_multiplier", IsSafetyField: false))
      .ToArray();

    Assert.Equal(
      RotaryEncoderConfigDefaults.VolumeClamp,
      RotaryEncoderConfigVerifier.VolumeClampFor(RotaryEncoderConfigVerifier.Classify(manyFeel, budget)));
  }

  [Fact]
  public void RetryBackoff_MatchesTheSpecifiedSchedule()
  {
    Assert.Equal(new[] { 250, 1000, 3000 }, RotaryEncoderConfigVerifier.RetryBackoffMs);
  }
}

/// <summary>
/// Guards the defaults against the device's own validation rules (ENC-11a).
///
/// <para>
/// These exist because a push that violates any of them is rejected <b>entirely</b> — the firmware's
/// <c>validate_config</c> returns false on the first bad encoder, so one wrong field on the tuning
/// knob silently discards the volume tiers too. That happened: <c>max_value = 0</c> on TUNING, taken
/// from a handoff table that marks the field "inert", made every encoder read back as factory.
/// Inert to the host is not ignored by the device.
/// </para>
/// </summary>
public class RotaryEncoderConfigDefaultsValidityTests
{
  [Fact]
  public void EveryEncoder_HasMinStrictlyBelowMax()
  {
    // Firmware: if (enc.min_value >= enc.max_value) return false;
    foreach (var (enc, i) in RotaryEncoderConfigDefaults.Create().Encoders.Select((e, i) => (e, i)))
    {
      Assert.True(enc.MinValue < enc.MaxValue,
        $"encoder {i} has min={enc.MinValue} max={enc.MaxValue}; the device rejects the whole config for this");
    }
  }

  [Fact]
  public void EveryEncoder_HasPositiveStepSize()
  {
    // Firmware: if (enc.step_size <= 0) return false;
    Assert.All(RotaryEncoderConfigDefaults.Create().Encoders, e => Assert.True(e.StepSize > 0));
  }

  [Fact]
  public void EnabledTiers_DescendInThresholdAndAscendInMultiplier()
  {
    // Firmware rejects a tier whose threshold is not strictly lower, or whose multiplier is not
    // strictly higher, than the previous enabled tier. A disabled tier (threshold 0) is skipped.
    foreach (var (enc, i) in RotaryEncoderConfigDefaults.Create().Encoders.Select((e, i) => (e, i)))
    {
      int prevThreshold = 0, prevMultiplier = 0;
      bool hasPrev = false;

      foreach (var tier in enc.Tiers)
      {
        if (tier.ThresholdMs == 0)
        {
          continue;
        }

        Assert.True(tier.Multiplier > 0, $"encoder {i}: an enabled tier must have a non-zero multiplier");

        if (hasPrev)
        {
          Assert.True(tier.ThresholdMs < prevThreshold, $"encoder {i}: tier thresholds must descend");
          Assert.True(tier.Multiplier > prevMultiplier, $"encoder {i}: tier multipliers must ascend");
        }

        prevThreshold = tier.ThresholdMs;
        prevMultiplier = tier.Multiplier;
        hasPrev = true;
      }
    }
  }
}
