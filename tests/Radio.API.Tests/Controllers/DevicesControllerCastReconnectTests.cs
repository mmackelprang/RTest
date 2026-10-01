using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.API.Services;
using Radio.API.Tests.TestSupport;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using IRadioConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-37 (review M1): every user action that touches the Cast output first takes it over from
/// the automatic reconnect, so the reconnect is never a second connecting party (AUD-85).
/// </summary>
public class DevicesControllerCastReconnectTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly CustomWebApplicationFactory<Program> _factory;
  private readonly Mock<ICastReconnectControl> _reconnect = new();

  public DevicesControllerCastReconnectTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _reconnect.Setup(r => r.CancelCastReconnectAsync()).Returns(Task.CompletedTask);
  }

  [Fact]
  public async Task SetOutputDevice_CancelsTheReconnectFirst()
  {
    await CreateController().SetOutputDevice(new SetOutputDeviceRequest { DeviceId = "" });

    _reconnect.Verify(r => r.CancelCastReconnectAsync(), Times.Once);
  }

  [Fact]
  public async Task ConnectToCastDevice_CancelsTheReconnectFirst()
  {
    await CreateController().ConnectToCastDevice(new ConnectCastDeviceRequest(), CancellationToken.None);

    _reconnect.Verify(r => r.CancelCastReconnectAsync(), Times.Once);
  }

  [Fact]
  public async Task DisconnectFromCastDevice_CancelsTheReconnectFirst()
  {
    await CreateController().DisconnectFromCastDevice(CancellationToken.None);

    _reconnect.Verify(r => r.CancelCastReconnectAsync(), Times.Once);
  }

  [Fact]
  public void TheControlTheControllerGets_IsTheSingletonServiceInstance()
  {
    // AddHostedService<T>() alone does not make the instance resolvable, and a second instance
    // would cancel a watcher that does not exist. (The test factory strips IHostedService
    // registrations, so the hosted half of Program.cs's wiring is not visible here; it resolves
    // the same singleton through GetRequiredService.)
    var control = _factory.Services.GetRequiredService<ICastReconnectControl>();

    Assert.Same(_factory.Services.GetRequiredService<AudioEngineInitializationService>(), control);
  }

  private DevicesController CreateController()
  {
    var preferences = new Mock<IOptionsMonitor<AudioPreferences>>();
    preferences.SetupGet(p => p.CurrentValue).Returns(new AudioPreferences());

    // No audio engine and no Cast output: each action returns early (400/503) — after the cancel.
    return new DevicesController(
      NullLogger<DevicesController>.Instance,
      new Mock<IAudioDeviceManager>().Object,
      new Mock<IRadioConfigurationManager>().Object,
      preferences.Object,
      Options.Create(new AudioOutputOptions()),
      castReconnect: _reconnect.Object);
  }
}
