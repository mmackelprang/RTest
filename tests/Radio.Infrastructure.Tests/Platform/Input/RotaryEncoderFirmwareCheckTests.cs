using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// <c>ENC-19</c>: the start-up check that the encoder's firmware processes host-to-device reports —
/// command <c>0x04</c> (read config) answered by a full 107-byte Input Report <c>0x02</c>. Flashing a
/// build older than RotaryUsb #11 silently reinstates the defect where every host write is accepted
/// and ignored; this is what makes that loud.
///
/// <para>
/// The device is a fake <see cref="Stream"/> that answers a read-config write by handing a report to
/// <see cref="HidRotaryEncoderService.TryClaimConfigReadBack"/> — the same method the read loop calls
/// for every report — synchronously, inside the write. The waiter is armed before the write, so an
/// answer can never race it; the never-answers cases need no answer at all, so nothing here depends on
/// timing except how long a failing run takes, which the test shortens.
/// </para>
/// </summary>
public class RotaryEncoderFirmwareCheckTests
{
  private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
  {
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable OnChange(Action<T, string?> listener) => new NullDisposable();
    private sealed class NullDisposable : IDisposable { public void Dispose() { } }
  }

  private enum Answer { FullConfigReport, ShortConfigReport, DiagnosticsOnly, Nothing, Disconnect }

  /// <summary>A device that answers each read-config request per a script, one entry per request.</summary>
  private sealed class ScriptedDevice(HidRotaryEncoderService service, params Answer[] script) : Stream
  {
    private int _requests;
    public int ReadConfigRequests => _requests;

    public override void Write(byte[] buffer, int offset, int count)
    {
      byte[] readConfig = RotaryEncoderConfigCodec.EncodeCommand(RotaryEncoderCommand.ReadConfig);
      if (!buffer.AsSpan(offset, count).SequenceEqual(readConfig))
      {
        return;
      }

      Answer answer = _requests < script.Length ? script[_requests] : Answer.Nothing;
      _requests++;

      switch (answer)
      {
        case Answer.FullConfigReport:
          byte[] full = RotaryEncoderConfigCodec.Encode(RotaryEncoderConfigDefaults.Create());
          Assert.Equal(107, full.Length);
          service.TryClaimConfigReadBack(full, full.Length);
          break;
        case Answer.ShortConfigReport:
          // A report 0x02 that is not the full configuration. The 107-byte length is part of the tell.
          byte[] shortReport = RotaryEncoderConfigCodec.Encode(RotaryEncoderConfigDefaults.Create());
          service.TryClaimConfigReadBack(shortReport, 64);
          break;
        case Answer.DiagnosticsOnly:
          // What the pre-#11 firmware kept sending: diagnostics, never a read-back.
          var diagnostics = new byte[56];
          diagnostics[0] = 0x04;
          service.TryClaimConfigReadBack(diagnostics, diagnostics.Length);
          break;
        case Answer.Disconnect:
          throw new IOException("device gone");
      }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set { } }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override long Seek(long offset, SeekOrigin origin) => 0;
    public override void SetLength(long value) { }
  }

  private static HidRotaryEncoderService BuildService()
  {
    var service = new HidRotaryEncoderService(
      NullLogger<HidRotaryEncoderService>.Instance,
      new StaticOptionsMonitor<RotaryEncoderOptions>(new RotaryEncoderOptions()),
      new RotaryEncoderDesignedConfig(NullLogger<RotaryEncoderDesignedConfig>.Instance));

    // Only the never-answers paths wait on these, and they wait for an answer that is never coming,
    // so shortening them changes how long a failing run takes and nothing about its verdict.
    service.ReadBackTimeout = TimeSpan.FromMilliseconds(20);
    service.FirmwareCheckBackoff = _ => TimeSpan.FromMilliseconds(1);
    return service;
  }

  [Fact]
  public async Task AFullReportTwo_Passes()
  {
    using var service = BuildService();
    var device = new ScriptedDevice(service, Answer.FullConfigReport);

    RotaryEncoderFirmwareCheck result = await service.CheckFirmwareAsync(device, CancellationToken.None);

    Assert.Equal(RotaryEncoderFirmwareCheck.Passed, result);
    Assert.Equal(1, device.ReadConfigRequests);
  }

  [Fact]
  public async Task ADeviceThatNeverAnswers_Fails_AfterTheWholeRetryBudget()
  {
    // The pre-RotaryUsb #11 defect: every write accepted, none acted on.
    using var service = BuildService();
    var device = new ScriptedDevice(service);

    RotaryEncoderFirmwareCheck result = await service.CheckFirmwareAsync(device, CancellationToken.None);

    Assert.Equal(RotaryEncoderFirmwareCheck.Failed, result);
    Assert.Equal(RotaryEncoderConfigVerifier.TransientAttempts, device.ReadConfigRequests);
  }

  [Fact]
  public async Task DiagnosticsWithoutAReadBack_Fails()
  {
    // "A broken one answers with diagnostics only" — the device is plainly alive, and that is not
    // the same as processing what the host sends it.
    using var service = BuildService();
    var device = new ScriptedDevice(service, Answer.DiagnosticsOnly, Answer.DiagnosticsOnly, Answer.DiagnosticsOnly);

    Assert.Equal(RotaryEncoderFirmwareCheck.Failed, await service.CheckFirmwareAsync(device, CancellationToken.None));
  }

  [Fact]
  public async Task AShortReportTwo_DoesNotCountAsAnAnswer()
  {
    using var service = BuildService();
    var device = new ScriptedDevice(service, Answer.ShortConfigReport, Answer.ShortConfigReport, Answer.ShortConfigReport);

    Assert.Equal(RotaryEncoderFirmwareCheck.Failed, await service.CheckFirmwareAsync(device, CancellationToken.None));
  }

  [Fact]
  public async Task AnAnswerOnTheLastAttempt_Passes()
  {
    // A USB peripheral missing a report on the first try is ordinary (ENC-11 §7.6). The check must not
    // call firmware broken on a miss the retry budget exists to absorb.
    using var service = BuildService();
    var device = new ScriptedDevice(service, Answer.Nothing, Answer.Nothing, Answer.FullConfigReport);

    Assert.Equal(RotaryEncoderFirmwareCheck.Passed, await service.CheckFirmwareAsync(device, CancellationToken.None));
  }

  [Fact]
  public async Task ADisconnectMidCheck_GivesNoVerdict()
  {
    // Gone is not broken. The reconnect runs the check again.
    using var service = BuildService();
    var device = new ScriptedDevice(service, Answer.Disconnect);

    Assert.Equal(RotaryEncoderFirmwareCheck.NotRun, await service.CheckFirmwareAsync(device, CancellationToken.None));
  }

  [Fact]
  public async Task ALaterReadBack_TurnsAFailedCheckIntoAPass()
  {
    // Re-apply from the Settings page, or a slow boot: the moment the device proves it processes
    // output reports, the verdict follows it.
    using var service = BuildService();
    await service.CheckFirmwareAsync(new ScriptedDevice(service), CancellationToken.None);
    Assert.Equal(RotaryEncoderFirmwareCheck.Failed, service.FirmwareCheck);

    byte[] full = RotaryEncoderConfigCodec.Encode(RotaryEncoderConfigDefaults.Create());
    service.TryClaimConfigReadBack(full, full.Length);

    Assert.Equal(RotaryEncoderFirmwareCheck.Passed, service.FirmwareCheck);
  }

  [Fact]
  public async Task ADisconnect_ClearsTheVerdict()
  {
    // The next device plugged in may run different firmware.
    using var service = BuildService();
    await service.CheckFirmwareAsync(new ScriptedDevice(service), CancellationToken.None);

    service.RaiseConnectionChanged(false);

    Assert.Equal(RotaryEncoderFirmwareCheck.NotRun, service.FirmwareCheck);
  }

  [Fact]
  public async Task TheHardFaultTransition_CarriesTheVerdict_ToTheEnc12Surfacing()
  {
    // The surfacing is ENC-12's: the tier change is what the badge and the toast listen to, so the
    // verdict has to be on it.
    using var service = BuildService();
    var seen = new List<EncoderConfigStatusEventArgs>();
    service.ConfigStatusChanged += (_, e) => seen.Add(e);

    await service.CheckFirmwareAsync(new ScriptedDevice(service), CancellationToken.None);
    service.ConfigStatus = RotaryEncoderConfigStatus.HardFault;

    EncoderConfigStatusEventArgs e = Assert.Single(seen);
    Assert.Equal(RotaryEncoderConfigStatus.HardFault, e.Status);
    Assert.Equal(RotaryEncoderFirmwareCheck.Failed, e.FirmwareCheck);
  }

  [Fact]
  public async Task TheProvisioningSnapshot_CarriesTheVerdict()
  {
    using var service = BuildService();
    await service.CheckFirmwareAsync(new ScriptedDevice(service), CancellationToken.None);

    Assert.Equal(RotaryEncoderFirmwareCheck.Failed, service.GetSnapshot().FirmwareCheck);
  }
}
