using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Controllers;
using Radio.API.Hubs;
using Radio.API.Models;
using Radio.API.Services;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Pins <c>POST /api/system/sleep</c>'s <c>panelOff</c> flag (<c>ENC-23</c>, the Sleep pill's hold):
/// sleep is entered first, then the panel is asked to power off once, and the response reports the
/// outcome.
///
/// <para>
/// A direct construction rather than <c>WebApplicationFactory</c>: on Windows (and in CI's container)
/// <c>Program.cs</c> registers no <see cref="IPanelPowerService"/>, so the only way to put one behind
/// the controller is to hand it one.
/// </para>
/// </summary>
public class SystemControllerDeepSleepTests
{
  private sealed class FakePanelPower : IPanelPowerService
  {
    private readonly Func<bool> _isSleeping;

    public FakePanelPower(Func<bool> isSleeping) { _isSleeping = isSleeping; }

    public PanelPowerOffResult Result { get; set; } = PanelPowerOffResult.PoweredOff;
    public List<string> PowerOffSources { get; } = [];

    /// <summary>What <c>IsSleeping</c> read at the moment of each <see cref="PowerOffNow"/> call.</summary>
    public List<bool> SleepingAtPowerOff { get; } = [];

    public bool IsPanelOff => false;

    public PanelInputOutcome OnEncoderInput(string source) => PanelInputOutcome.Pass;

    public PanelPowerOffResult PowerOffNow(string source)
    {
      PowerOffSources.Add(source);
      SleepingAtPowerOff.Add(_isSleeping());
      return Result;
    }
  }

  private static SleepService CreateSleepService()
  {
    var hub = new Mock<IHubContext<AudioStateHub>>();
    var clients = new Mock<IHubClients>();
    clients.SetupGet(c => c.All).Returns(new Mock<IClientProxy>().Object);
    hub.SetupGet(h => h.Clients).Returns(clients.Object);
    return new SleepService(NullLogger<SleepService>.Instance, hub.Object);
  }

  private static SystemController CreateController(SleepService sleep, IPanelPowerService? panel) =>
    new(
      NullLogger<SystemController>.Instance,
      new Mock<IAudioEngine>().Object,
      new Mock<IHostApplicationLifetime>().Object,
      sleep,
      panel);

  private static JsonElement Body(IActionResult result)
  {
    var ok = Assert.IsType<OkObjectResult>(result);
    return JsonSerializer.SerializeToElement(ok.Value);
  }

  [Fact]
  public async Task PanelOff_EntersSleepFirst_ThenPowersOffOnce()
  {
    SleepService sleep = CreateSleepService();
    var panel = new FakePanelPower(() => sleep.IsSleeping);
    SystemController controller = CreateController(sleep, panel);

    JsonElement body = Body(await controller.SetSleepState(new SetSleepRequest { Sleep = true, PanelOff = true }));

    Assert.True(sleep.IsSleeping);
    Assert.Equal(["sleep-pill-hold"], panel.PowerOffSources);
    Assert.Equal([true], panel.SleepingAtPowerOff);
    Assert.True(body.GetProperty("isSleeping").GetBoolean());
    Assert.True(body.GetProperty("panelOff").GetBoolean());
    Assert.Equal("PoweredOff", body.GetProperty("panelOffResult").GetString());
  }

  [Fact]
  public async Task PanelOff_AlreadyOff_ReportsPanelOffTrue()
  {
    SleepService sleep = CreateSleepService();
    var panel = new FakePanelPower(() => sleep.IsSleeping) { Result = PanelPowerOffResult.AlreadyOff };

    JsonElement body = Body(await CreateController(sleep, panel)
      .SetSleepState(new SetSleepRequest { Sleep = true, PanelOff = true }));

    Assert.True(body.GetProperty("panelOff").GetBoolean());
    Assert.Equal("AlreadyOff", body.GetProperty("panelOffResult").GetString());
  }

  [Fact]
  public async Task PanelOff_Refused_StillSleeps_AndSaysWhy()
  {
    SleepService sleep = CreateSleepService();
    var panel = new FakePanelPower(() => sleep.IsSleeping) { Result = PanelPowerOffResult.RefusedEncoderNotConnected };

    JsonElement body = Body(await CreateController(sleep, panel)
      .SetSleepState(new SetSleepRequest { Sleep = true, PanelOff = true }));

    Assert.True(sleep.IsSleeping);
    Assert.False(body.GetProperty("panelOff").GetBoolean());
    Assert.Equal("RefusedEncoderNotConnected", body.GetProperty("panelOffResult").GetString());
  }

  [Fact]
  public async Task NoPanelOff_DoesNotTouchThePanel_AndKeepsTheOriginalShape()
  {
    SleepService sleep = CreateSleepService();
    var panel = new FakePanelPower(() => sleep.IsSleeping);

    JsonElement body = Body(await CreateController(sleep, panel)
      .SetSleepState(new SetSleepRequest { Sleep = true }));

    Assert.True(sleep.IsSleeping);
    Assert.Empty(panel.PowerOffSources);
    Assert.True(body.GetProperty("isSleeping").GetBoolean());
    Assert.False(body.TryGetProperty("panelOff", out _));
  }

  [Fact]
  public async Task PanelOff_OnAWake_IsIgnored()
  {
    SleepService sleep = CreateSleepService();
    await sleep.EnterSleepAsync();
    var panel = new FakePanelPower(() => sleep.IsSleeping);

    JsonElement body = Body(await CreateController(sleep, panel)
      .SetSleepState(new SetSleepRequest { Sleep = false, PanelOff = true }));

    Assert.False(sleep.IsSleeping);
    Assert.Empty(panel.PowerOffSources);
    Assert.False(body.TryGetProperty("panelOff", out _));
  }

  [Fact]
  public async Task PanelOff_WithNoPanelService_SleepsAndReportsUnavailable()
  {
    SleepService sleep = CreateSleepService();

    JsonElement body = Body(await CreateController(sleep, panel: null)
      .SetSleepState(new SetSleepRequest { Sleep = true, PanelOff = true }));

    Assert.True(sleep.IsSleeping);
    Assert.False(body.GetProperty("panelOff").GetBoolean());
    Assert.Equal("Unavailable", body.GetProperty("panelOffResult").GetString());
  }
}
