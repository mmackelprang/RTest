using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers ENC-12 Task 1: the configuration tier is observable, and observable exactly once per real
/// change.
///
/// <para>
/// The push loop assigns <c>ConfigStatus</c> once per attempt and may assign the same value several
/// times running — <c>Transient</c> on attempts 1 and 2 is the ordinary case — so a broadcast per
/// assignment would put SignalR traffic on the wire for a state that did not change, on a box where
/// incidental load correlates with audible audio distortion. The change detection lives in the
/// property setter rather than at the assignment sites so no caller can forget it, and these tests
/// drive that setter directly.
/// </para>
///
/// <para>
/// The setter and <c>RaiseConnectionChanged</c> are <c>internal</c> for exactly this reason: the
/// tier is otherwise only reachable by attaching real HID hardware.
/// </para>
/// </summary>
public class RotaryEncoderConfigStatusEventTests
{
  // Copied rather than shared with RotaryEncoderMaintenanceChannelTests: making that file's private
  // helper public to reuse it here would widen a test-only seam across the whole test project for
  // the sake of one call site.
  private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
  {
    private readonly T _value;
    public StaticOptionsMonitor(T value) { _value = value; }
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable OnChange(Action<T, string?> listener) => new NullDisposable();

    private sealed class NullDisposable : IDisposable { public void Dispose() { } }
  }

  private static HidRotaryEncoderService BuildService() =>
    new(
      NullLogger<HidRotaryEncoderService>.Instance,
      new StaticOptionsMonitor<RotaryEncoderOptions>(new RotaryEncoderOptions()),
      new RotaryEncoderDesignedConfig(NullLogger<RotaryEncoderDesignedConfig>.Instance));

  [Fact]
  public void ConfigStatus_RaisesOnceWhenItChanges()
  {
    using var service = BuildService();
    int raised = 0;
    service.ConfigStatusChanged += (_, _) => raised++;

    service.ConfigStatus = RotaryEncoderConfigStatus.Configured;

    Assert.Equal(1, raised);
    Assert.Equal(RotaryEncoderConfigStatus.Configured, service.ConfigStatus);
  }

  [Fact]
  public void ConfigStatus_DoesNotRaiseWhenAssignedTheSameValue()
  {
    // The retry loop's ordinary shape: Transient on attempt 1, and again on 2 and 3.
    using var service = BuildService();
    int raised = 0;
    service.ConfigStatusChanged += (_, _) => raised++;

    service.ConfigStatus = RotaryEncoderConfigStatus.Transient;
    service.ConfigStatus = RotaryEncoderConfigStatus.Transient;
    service.ConfigStatus = RotaryEncoderConfigStatus.Transient;

    Assert.Equal(1, raised);
  }

  [Fact]
  public void ConfigStatusChanged_CarriesThePreviousTier()
  {
    using var service = BuildService();
    service.ConfigStatus = RotaryEncoderConfigStatus.Transient;

    EncoderConfigStatusEventArgs? seen = null;
    service.ConfigStatusChanged += (_, e) => seen = e;

    service.ConfigStatus = RotaryEncoderConfigStatus.HardFault;

    Assert.NotNull(seen);
    Assert.Equal(RotaryEncoderConfigStatus.HardFault, seen!.Status);
    Assert.Equal(RotaryEncoderConfigStatus.Transient, seen.PreviousStatus);
  }

  [Fact]
  public void Disconnect_ResetsTheTier_SoAnAbsentDeviceIsNeverReportedConfigured()
  {
    // The app cannot know what an unplugged device is running, so a device that was Configured and
    // is then unplugged must not keep claiming it. The reset lives in RaiseConnectionChanged rather
    // than at its five call sites, so a sixth added later cannot reintroduce the stale tier.
    using var service = BuildService();
    service.ConfigStatus = RotaryEncoderConfigStatus.Configured;

    var tiers = new List<RotaryEncoderConfigStatus>();
    service.ConfigStatusChanged += (_, e) => tiers.Add(e.Status);

    service.RaiseConnectionChanged(false);

    Assert.Equal(RotaryEncoderConfigStatus.Unknown, service.ConfigStatus);
    Assert.Equal(new[] { RotaryEncoderConfigStatus.Unknown }, tiers);
  }

  [Fact]
  public void Connect_DoesNotResetTheTier()
  {
    // Only absence invalidates what we know. A reconnect that reported Unknown would make the badge
    // flicker through a fault state on every successful plug-in.
    using var service = BuildService();
    service.ConfigStatus = RotaryEncoderConfigStatus.Configured;

    service.RaiseConnectionChanged(true);

    Assert.Equal(RotaryEncoderConfigStatus.Configured, service.ConfigStatus);
  }

  [Fact]
  public void TheResetTierCarriesTheTightVolumeClamp()
  {
    // The same value drives the host's per-event volume clamp, so resetting to Unknown on
    // disconnect also tightens the clamp — the correct direction for a device nobody can verify.
    Assert.Equal(RotaryEncoderConfigDefaults.VolumeClampUnverified,
      RotaryEncoderConfigVerifier.VolumeClampFor(RotaryEncoderConfigStatus.Unknown));
  }
}
