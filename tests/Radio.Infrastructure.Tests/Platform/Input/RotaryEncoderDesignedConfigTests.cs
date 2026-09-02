using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Configuration;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers ENC-8 Task 6: the designed table plus the owner's direction overrides, resolved once and
/// used by the wire, the flash and the screen alike.
/// </summary>
public class RotaryEncoderDesignedConfigTests
{
  private static RotaryEncoderDesignedConfig BuildSut() =>
    new(NullLogger<RotaryEncoderDesignedConfig>.Instance);

  [Fact]
  public async Task Resolve_WithNoOverrides_IsByteIdenticalToTheDesignedTable()
  {
    RotaryEncoderDesignedConfig sut = BuildSut();
    Assert.Equal(
      RotaryEncoderConfigCodec.Encode(RotaryEncoderConfigDefaults.Create()),
      RotaryEncoderConfigCodec.Encode(await sut.ResolveAsync()));
  }

  [Fact]
  public async Task Resolve_AppliesAReverseOverride_WithoutMutatingTheDesignedTable()
  {
    RotaryEncoderDesignedConfig sut = BuildSut();
    await sut.SetReverseAsync(2, true);

    Assert.True((await sut.ResolveAsync()).Encoders[2].Reverse);
    // The designed table is a separate, unchanged thing — the verifier tests assert this too.
    Assert.False(RotaryEncoderConfigDefaults.Create().Encoders[2].Reverse);
  }

  [Fact]
  public async Task AReversedKnob_VerifiesAsConfigured_AndDoesNotBecomeAHardFault()
  {
    // The trap this test exists for: `reverse` is a SAFETY field, so if the push carried the override
    // but the comparison still expected the designed `false`, every knob the owner reverses would sit
    // in HardFault with the volume clamp tightened to 2 units per event, forever.
    RotaryEncoderDesignedConfig sut = BuildSut();
    await sut.SetReverseAsync(0, true);

    RotaryEncoderDeviceConfig desired = await sut.ResolveAsync();
    byte[] wire = RotaryEncoderConfigCodec.Encode(desired);
    Assert.True(RotaryEncoderConfigCodec.TryDecode(wire, wire.Length, out var deviceEcho));

    Assert.Empty(RotaryEncoderConfigVerifier.Compare(desired, deviceEcho));
    Assert.Equal(
      RotaryEncoderConfigStatus.Configured,
      RotaryEncoderConfigVerifier.Classify(RotaryEncoderConfigVerifier.Compare(desired, deviceEcho), attempt: 1));
  }

  [Fact]
  public async Task AReversedKnobComparedAgainstTheDesignedTable_WouldBeAHardFault()
  {
    // The negative half of the test above: this is exactly what happens if the override reaches the
    // wire but not the verifier, and it is why ApplyConfigurationAsync compares against the same
    // resolved object it encoded rather than against RotaryEncoderConfigDefaults.Create().
    RotaryEncoderDesignedConfig sut = BuildSut();
    await sut.SetReverseAsync(0, true);

    RotaryEncoderDeviceConfig resolved = await sut.ResolveAsync();
    byte[] wire = RotaryEncoderConfigCodec.Encode(resolved);
    Assert.True(RotaryEncoderConfigCodec.TryDecode(wire, wire.Length, out var deviceEcho));

    var mismatches = RotaryEncoderConfigVerifier.Compare(RotaryEncoderConfigDefaults.Create(), deviceEcho);

    Assert.Equal(
      RotaryEncoderConfigStatus.HardFault,
      RotaryEncoderConfigVerifier.Classify(mismatches, attempt: 1));
    Assert.Equal(
      RotaryEncoderConfigDefaults.VolumeClampUnverified,
      RotaryEncoderConfigVerifier.VolumeClampFor(RotaryEncoderConfigStatus.HardFault));
  }

  [Fact]
  public async Task SetReverse_RejectsAnIndexOffTheFace()
  {
    RotaryEncoderDesignedConfig sut = BuildSut();

    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.SetReverseAsync(-1, true));
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
      () => sut.SetReverseAsync(RotaryEncoderDeviceConfig.EncoderCount, true));
  }

  [Fact]
  public async Task WithNoConfigurationStore_NothingAboutAFlashWriteIsClaimedToBeRetained()
  {
    // No store means no persistence, and the status card must not read "saved" off a value that
    // does not exist. Both readers answer null, which is what renders as "never saved".
    RotaryEncoderDesignedConfig sut = BuildSut();

    await sut.RecordFlashWriteAsync(DateTimeOffset.UtcNow, "DEADBEEF");

    Assert.Null(await sut.GetLastSavedHashAsync());
    Assert.Null(await sut.GetLastSavedUtcAsync());
  }
}
