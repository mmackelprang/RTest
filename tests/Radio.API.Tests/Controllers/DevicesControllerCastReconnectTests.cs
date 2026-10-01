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
  public async Task ConnectToCastDevice_WithNoCastOutput_Returns503_WithoutTouchingTheReconnect()
  {
    // AUD-85 (re-review LOW-1): request validation precedes the cancel. A valid request's
    // pick-aware cancel — which keeps a same-device reconnect's connection and waits the longer
    // bound, not the short one — is covered by DevicesControllerCastPickReconnectTests.
    var result = await CreateController().ConnectToCastDevice(
      new ConnectCastDeviceRequest { DeviceId = "cast-a", IpAddress = "192.0.2.10" }, CancellationToken.None);

    Assert.Equal(503, Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(result).StatusCode);
    _reconnect.Verify(r => r.CancelCastReconnectForCastPickAsync(It.IsAny<string?>()), Times.Never);
    _reconnect.Verify(r => r.CancelCastReconnectAsync(), Times.Never);
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

    // No audio engine and no Cast output: each action returns early (400/503) — after the cancel,
    // except cast/connect, which validates first (AUD-85 re-review LOW-1).
    return new DevicesController(
      NullLogger<DevicesController>.Instance,
      new Mock<IAudioDeviceManager>().Object,
      new Mock<IRadioConfigurationManager>().Object,
      preferences.Object,
      Options.Create(new AudioOutputOptions()),
      castReconnect: _reconnect.Object);
  }
}
