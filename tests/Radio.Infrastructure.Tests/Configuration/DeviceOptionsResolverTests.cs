using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

namespace Radio.Infrastructure.Tests.Configuration;

public class DeviceOptionsResolverTests
{
  private readonly Mock<IOptionsMonitor<DeviceOptions>> _optionsMonitor;
  private readonly Mock<IConfigurationManager> _configManager;
  private readonly ILogger<DeviceOptionsResolver> _logger;

  public DeviceOptionsResolverTests()
  {
    _optionsMonitor = new Mock<IOptionsMonitor<DeviceOptions>>();
    _optionsMonitor.Setup(x => x.CurrentValue).Returns(new DeviceOptions
    {
      Radio = new RadioDeviceOptions { USBPort = "fallback-radio" },
      Vinyl = new VinylDeviceOptions { USBPort = "fallback-vinyl" },
    });

    _configManager = new Mock<IConfigurationManager>();
    _configManager.Setup(x => x.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);

    _logger = NullLoggerFactory.Instance.CreateLogger<DeviceOptionsResolver>();
  }

  [Fact]
  public async Task GetDeviceOptionsAsync_ReadsFromConfigStore()
  {
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Radio", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("{\"usbPort\":\"AB13X\"}");
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Vinyl", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("{\"usbPort\":\"TurntableUSB\"}");

    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, _configManager.Object);

    var result = await resolver.GetDeviceOptionsAsync();

    Assert.Equal("AB13X", result.Radio.USBPort);
    Assert.Equal("TurntableUSB", result.Vinyl.USBPort);
  }

  [Fact]
  public async Task GetDeviceOptionsAsync_FallsBackToOptionsMonitor_WhenConfigStoreEmpty()
  {
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", It.IsAny<string>(), It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);

    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, _configManager.Object);

    var result = await resolver.GetDeviceOptionsAsync();

    Assert.Equal("fallback-radio", result.Radio.USBPort);
    Assert.Equal("fallback-vinyl", result.Vinyl.USBPort);
  }

  [Fact]
  public async Task GetDeviceOptionsAsync_FallsBackToOptionsMonitor_WhenNoConfigManager()
  {
    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, configManager: null);

    var result = await resolver.GetDeviceOptionsAsync();

    Assert.Equal("fallback-radio", result.Radio.USBPort);
    Assert.Equal("fallback-vinyl", result.Vinyl.USBPort);
  }

  [Fact]
  public async Task GetDeviceOptionsAsync_FallsBackToOptionsMonitor_WhenConfigStoreThrows()
  {
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", It.IsAny<string>(), It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("Store unavailable"));

    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, _configManager.Object);

    var result = await resolver.GetDeviceOptionsAsync();

    Assert.Equal("fallback-radio", result.Radio.USBPort);
  }

  [Fact]
  public async Task GetRadioUSBPortAsync_ReturnsConfigStoreValue()
  {
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Radio", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("{\"usbPort\":\"AB13X\"}");
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Vinyl", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Cast", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);

    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, _configManager.Object);

    var port = await resolver.GetRadioUSBPortAsync();

    Assert.Equal("AB13X", port);
  }

  [Fact]
  public async Task GetVinylUSBPortAsync_ReturnsConfigStoreValue()
  {
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Radio", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Vinyl", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("{\"usbPort\":\"TurntableUSB\"}");
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Cast", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);

    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, _configManager.Object);

    var port = await resolver.GetVinylUSBPortAsync();

    Assert.Equal("TurntableUSB", port);
  }

  [Fact]
  public async Task GetDeviceOptionsAsync_HandlesInvalidJson_GracefullyFallsBack()
  {
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Radio", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("not-valid-json");
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Vinyl", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Cast", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);

    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, _configManager.Object);

    var result = await resolver.GetDeviceOptionsAsync();

    // Invalid JSON for Radio falls back to IOptionsMonitor value
    Assert.Equal("fallback-radio", result.Radio.USBPort);
  }

  [Fact]
  public async Task GetDeviceOptionsAsync_MixesConfigStoreAndFallback()
  {
    // Radio configured in config store, Vinyl not
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Radio", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("{\"usbPort\":\"AB13X\"}");
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Vinyl", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);
    _configManager.Setup(x => x.GetValueAsync<string>("sqlite", "devices:Cast", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);

    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, _configManager.Object);

    var result = await resolver.GetDeviceOptionsAsync();

    Assert.Equal("AB13X", result.Radio.USBPort);
    Assert.Equal("fallback-vinyl", result.Vinyl.USBPort);
  }

  [Fact]
  public async Task GetDeviceOptionsAsync_UsesJsonConfigStoreId_WhenNotSqlite()
  {
    _configManager.Setup(x => x.CurrentStoreType).Returns(ConfigurationStoreType.Json);
    _configManager.Setup(x => x.GetValueAsync<string>("config", "devices:Radio", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("{\"usbPort\":\"AB13X\"}");
    _configManager.Setup(x => x.GetValueAsync<string>("config", "devices:Vinyl", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);
    _configManager.Setup(x => x.GetValueAsync<string>("config", "devices:Cast", It.IsAny<ConfigurationReadMode>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);

    var resolver = new DeviceOptionsResolver(_logger, _optionsMonitor.Object, _configManager.Object);

    var result = await resolver.GetDeviceOptionsAsync();

    Assert.Equal("AB13X", result.Radio.USBPort);
  }
}
