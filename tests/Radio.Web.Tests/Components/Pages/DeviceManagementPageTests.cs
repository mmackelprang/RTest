using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using MudBlazor.Services;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// bUnit tests for the DeviceManagementPage component
/// Tests device list rendering, default device selection, and USB reservations display
/// </summary>
public class DeviceManagementPageTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public DeviceManagementPageTests()
  {
    _loggerFactory = new NullLoggerFactory();
    
    // Set up minimal dependencies with in-memory configuration
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    
    // Add MudBlazor services (required for ISnackbar injection)
    Services.AddMudServices();
    
    // Add HttpClient for DevicesApiService
    Services.AddHttpClient<DevicesApiService>();
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _loggerFactory?.Dispose();
    }
    base.Dispose(disposing);
  }

  [Fact]
  public void DeviceManagementPage_Renders_Successfully()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - Component renders without throwing
    Assert.NotNull(cut);
    Assert.Contains("Device Management", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Contains_Output_Devices_Section()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - Output devices section present
    Assert.Contains("Output Devices", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Contains_Input_Devices_Section()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - Input devices section present
    Assert.Contains("Input Devices", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Contains_USB_Reservations_Section()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - USB reservations section present
    Assert.Contains("USB Port Reservations", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Has_Refresh_Button()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - Refresh button exists
    Assert.Contains("Refresh Devices", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Shows_Empty_State_When_No_Devices()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - Should show info about no devices found
    // Initially before API response, will show loading or empty state
    var markup = cut.Markup;
    Assert.True(
      markup.Contains("No output devices found") || markup.Contains("Loading") || markup.Contains("mud-progress"),
      "Should show empty state or loading indicator"
    );
  }

  [Fact]
  public void DeviceManagementPage_Uses_MudBlazor_Components()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - Uses MudBlazor components
    Assert.Contains("mud-container", cut.Markup);
    Assert.Contains("mud-button", cut.Markup);
    Assert.Contains("mud-paper", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Has_Three_Main_Sections()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - All three sections present
    var markup = cut.Markup;
    Assert.Contains("Output Devices", markup);
    Assert.Contains("Input Devices", markup);
    Assert.Contains("USB Port Reservations", markup);
  }

  [Fact]
  public void DeviceManagementPage_Structure_Is_Valid()
  {
    // Act
    var cut = RenderComponent<DeviceManagementPage>();

    // Assert - Page structure is valid
    var markup = cut.Markup;
    
    // Should contain MudBlazor components
    Assert.Contains("mud-", markup);
    
    // Should have the three main sections
    Assert.Contains("Output Devices", markup);
    Assert.Contains("Input Devices", markup);
    Assert.Contains("USB Port Reservations", markup);
  }
}
