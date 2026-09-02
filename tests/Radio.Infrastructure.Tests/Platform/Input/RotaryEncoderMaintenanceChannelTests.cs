using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers ENC-8 Task 5: the read loop is the only reader, and it hands a configuration read-back to
/// whoever asked for one.
///
/// <para>
/// The I/O is not what is under test — the <b>handoff</b> is. A maintenance command arrives on an
/// ASP.NET request thread, writes, and then waits for a report that only the read loop will ever
/// see; these tests drive that completion path directly.
/// </para>
/// </summary>
public class RotaryEncoderMaintenanceChannelTests
{
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

  /// <summary>A full report 0x02 buffer carrying the designed configuration.</summary>
  private static byte[] ConfigReport() =>
    RotaryEncoderConfigCodec.Encode(RotaryEncoderConfigDefaults.Create());

  /// <summary>A report 0x01 buffer — the positions report the knobs actually send.</summary>
  private static byte[] PositionsReport()
  {
    var buffer = new byte[RotaryEncoderDecoder.PositionPayloadSize + 1];
    buffer[0] = 0x01;
    return buffer;
  }

  [Fact]
  public async Task ConfigReport_CompletesAnOutstandingReadBackRequest()
  {
    using var service = BuildService();
    Task<RotaryEncoderDeviceConfig> pending = service.ArmConfigReadBack().Task;

    byte[] report = ConfigReport();
    Assert.True(service.TryClaimConfigReadBack(report, report.Length));

    RotaryEncoderDeviceConfig readBack = await pending.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.True(RotaryEncoderConfigCodec.Matches(RotaryEncoderConfigDefaults.Create(), readBack));
  }

  [Fact]
  public void ConfigReport_WithNoOutstandingRequest_IsIgnoredAndDoesNotThrow()
  {
    // The device can emit 0x02 unprompted after a power cycle. It is still claimed - it must not
    // fall through to the positions decoder - but there is nobody to hand it to.
    using var service = BuildService();

    byte[] report = ConfigReport();

    Assert.True(service.TryClaimConfigReadBack(report, report.Length));
  }

  [Fact]
  public void PositionsReport_DoesNotCompleteAnOutstandingReadBackRequest()
  {
    // A knob turned while a read-back is outstanding must not be mistaken for the device's answer.
    using var service = BuildService();
    Task<RotaryEncoderDeviceConfig> pending = service.ArmConfigReadBack().Task;

    byte[] report = PositionsReport();

    Assert.False(service.TryClaimConfigReadBack(report, report.Length));
    Assert.False(pending.IsCompleted);
  }

  [Fact]
  public async Task Disconnect_FailsAnOutstandingReadBackRequest_RatherThanLeavingItToTimeOut()
  {
    // "The device went away" is the honest answer, and it is available immediately. Letting the
    // caller sit out the 2 s read-back timeout would report "the device did not confirm" instead,
    // which is a different claim.
    using var service = BuildService();
    Task<RotaryEncoderDeviceConfig> pending = service.ArmConfigReadBack().Task;

    service.FailPendingConfigRead(new IOException("Encoder disconnected."));

    await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
  }
}
