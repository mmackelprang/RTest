using Microsoft.Extensions.Logging.Abstractions;
using Radio.Infrastructure.Platform.Bluetooth.Native;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth.Native;

/// <summary>
/// AUD-11 Tasks 1 and 2: the capture stream's targeting contract, pinned without a PipeWire daemon.
/// </summary>
/// <remarks>
/// ⚠ What these tests CANNOT show: that WirePlumber honours <c>node.dont-reconnect</c>. They pin that
/// we ASK. Whether the session manager obeys is only observable on the appliance (plan AUD-10 §5.1).
///
/// ⚠ Nor do they cover the caller-side zero-serial refusal in
/// <c>LinuxBluetoothService.StartCaptureSubprocess</c>: that path starts the pw-record fallback, a
/// real subprocess. Only the constructor's defence-in-depth guard is pinned here.
/// </remarks>
public class PipeWireNativeStreamPropertiesTests
{
  [Fact]
  public void BuildStreamProperties_SetsDontReconnectTrue()
  {
    // ⛔ "= true" is part of the assertion on purpose: "node.dont-reconnect = false" is the default
    // and contains the bare token too.
    Assert.Contains("node.dont-reconnect = true", PipeWireNativeStream.BuildStreamProperties(1234u));
  }

  [Fact]
  public void BuildStreamProperties_KeepsAutoconnect()
  {
    // Plan AUD-11 §6.2 holds dropping autoconnect in reserve; pinned so removing it is deliberate.
    Assert.Contains("node.autoconnect = true", PipeWireNativeStream.BuildStreamProperties(1234u));
  }

  [Fact]
  public void BuildStreamProperties_PutsTheSerialInTargetObject()
  {
    Assert.Contains("target.object = 58968", PipeWireNativeStream.BuildStreamProperties(58968u));
  }

  [Fact]
  public void BuildStreamProperties_IsBalancedAndSingleLine()
  {
    var props = PipeWireNativeStream.BuildStreamProperties(1234u);
    Assert.Equal(1, props.Count(c => c == '{'));
    Assert.Equal(1, props.Count(c => c == '}'));
    Assert.DoesNotContain('\n', props);
  }

  [Fact]
  public void BuildStreamProperties_ExactShape()
  {
    Assert.Equal(
      "{ media.type = Audio media.category = Capture media.role = Music "
      + "node.autoconnect = true node.dont-reconnect = true target.object = 1234 }",
      PipeWireNativeStream.BuildStreamProperties(1234u));
  }

  [Fact]
  public void Constructor_ZeroTargetSerial_Throws()
  {
    // The guard is the first statement in the constructor, before EnsurePwInit's native pw_init —
    // which is why this can run on Windows at all.
    Assert.Throws<ArgumentOutOfRangeException>(() => new PipeWireNativeStream(
      0u, 48000, 2, (_, _) => { }, NullLogger.Instance));
  }

  [SkippableFact]
  public void Constructor_NonZeroTargetSerial_DoesNotThrow()
  {
    // Needs libpipewire: the constructor calls pw_init. Skipped wherever it cannot be loaded —
    // including a Linux CI runner without PipeWire installed, not just Windows.
    Skip.IfNot(
      OperatingSystem.IsLinux()
        && System.Runtime.InteropServices.NativeLibrary.TryLoad("libpipewire-0.3.so.0", out _),
      "PipeWireNativeStream's constructor calls pw_init (libpipewire-0.3 not loadable here).");
    using var stream = new PipeWireNativeStream(58968u, 48000, 2, (_, _) => { }, NullLogger.Instance);
    Assert.Equal(58968u, stream.TargetNodeSerial);
  }
}
