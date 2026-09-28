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
    // Needs libpipewire: the constructor calls pw_init through [DllImport("pipewire-0.3")].
    //
    // TEST-11: the guard must ask the SAME question the P/Invoke asks, or it fails where it meant
    // to skip. The (string, Assembly, DllImportSearchPath?) overload applies DllImport's own
    // probing — `libpipewire-0.3.so`, `pipewire-0.3.so`, `libpipewire-0.3`, `pipewire-0.3` — which
    // is what the runtime does for the constructor's pw_init. The plain TryLoad(string) overload is
    // a bare dlopen and does not, so the previous guard (`TryLoad("libpipewire-0.3.so.0")`) found
    // the versioned soname that the runtime package ships and declared PipeWire loadable, while the
    // P/Invoke then probed for the UNVERSIONED `libpipewire-0.3.so` — a symlink only
    // `libpipewire-0.3-dev` installs — and threw DllNotFoundException. Measured 2026-09-28 on a box
    // with the runtime package alone (fails) and again after the dev package (passes); the CI runner
    // image is in the first state, which is why CI on main was red from 2026-09-26.
    //
    // So this skips on Windows, on a Linux box with no PipeWire at all, AND on a Linux box that has
    // the runtime library but not the dev symlink. It runs only where pw_init would actually load.
    Skip.IfNot(
      OperatingSystem.IsLinux()
        && System.Runtime.InteropServices.NativeLibrary.TryLoad(
          "pipewire-0.3", typeof(PipeWireNative).Assembly, searchPath: null, out _),
      "PipeWireNativeStream's constructor calls pw_init, and [DllImport(\"pipewire-0.3\")] cannot "
        + "resolve here (needs the unversioned libpipewire-0.3.so, i.e. libpipewire-0.3-dev).");
    using var stream = new PipeWireNativeStream(58968u, 48000, 2, (_, _) => { }, NullLogger.Instance);
    Assert.Equal(58968u, stream.TargetNodeSerial);
  }
}
