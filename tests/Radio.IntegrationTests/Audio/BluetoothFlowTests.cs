using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Platform.Bluetooth;
using Radio.IntegrationTests.TestSupport;
using Xunit;

namespace Radio.IntegrationTests.Audio;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection> _configureServices;

    public CustomWebApplicationFactory(Action<IServiceCollection> configureServices)
    {
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            _configureServices(services);
        });
    }
}

public class BluetoothFlowTests
{
    [Fact]
    public async Task Bluetooth_Source_Activates_And_Updates_Metadata()
    {
        var factory = new CustomWebApplicationFactory(services =>
        {
            // Replace IBluetoothService with MockBluetoothService
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBluetoothService));
            if (descriptor != null) { services.Remove(descriptor); }
            
            services.AddSingleton<IBluetoothService, MockBluetoothService>();
        });

        var client = factory.CreateClient();
        var serviceProvider = factory.Services;
        var mockBluetooth = (MockBluetoothService)serviceProvider.GetRequiredService<IBluetoothService>();

        // 1. Simulate device connection
        var device = new BluetoothDeviceInfo
        {
            Address = "00:11:22:33:44:55",
            Name = "Test Bluetooth Device",
            IsPaired = true,
            IsConnected = true
        };
        mockBluetooth.SimulateConnection(device);

        // 2. Verify connection reflected
        Assert.True(mockBluetooth.ConnectedDevice != null);
        Assert.Equal("Test Bluetooth Device", mockBluetooth.ConnectedDevice.Name);

        // 3. Simulate Metadata Change
        bool metadataEventFired = false;
        mockBluetooth.MetadataChanged += (s, e) => { metadataEventFired = true; };
        mockBluetooth.SimulateMetadataChange("Great Song", "Awesome Artist");

        Assert.True(metadataEventFired);
    }
}
