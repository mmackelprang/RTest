using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using MudBlazor;
using MudBlazor.Services;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// bUnit tests for the DeviceManagementPage component.
/// Uses vertical MudTabs layout — only the active tab panel content is rendered.
/// </summary>
public class DeviceManagementPageTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public DeviceManagementPageTests()
  {
    _loggerFactory = new NullLoggerFactory();

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", "http://localhost:5000" }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    Services.AddMudServices();
    JSInterop.Mode = JSRuntimeMode.Loose;
    ComponentFactories.AddStub<MudPopoverProvider>();
    Services.AddHttpClient<DevicesApiService>();
    Services.AddScoped<Radio.Web.Services.DeviceDisplayStateService>();
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
    var cut = RenderComponent<DeviceManagementPage>();

    Assert.NotNull(cut);
    // Active tab (Display) shows its content
    Assert.Contains("Device Display Settings", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Contains_Output_Devices_Tab()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    // Tab label is rendered even when inactive
    Assert.Contains("Outputs", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Contains_Input_Devices_Tab()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    Assert.Contains("Inputs", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Contains_USB_Reservations_Tab()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    Assert.Contains("USB Ports", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Has_Refresh_Button()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    // Refresh button on the active Display tab
    Assert.Contains("Refresh", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Shows_Empty_State_When_No_Devices()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    // Display tab shows empty state or loading indicator when API hasn't responded
    var markup = cut.Markup;
    Assert.True(
      markup.Contains("No devices found") || markup.Contains("mud-progress"),
      "Should show empty state or loading indicator"
    );
  }

  [Fact]
  public void DeviceManagementPage_Uses_MudBlazor_Components()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    Assert.Contains("mud-tabs", cut.Markup);
    Assert.Contains("mud-button", cut.Markup);
  }

  [Fact]
  public void DeviceManagementPage_Has_Five_Tab_Sections()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    var markup = cut.Markup;
    Assert.Contains("Display", markup);
    Assert.Contains("Outputs", markup);
    Assert.Contains("Cast", markup);
    Assert.Contains("Inputs", markup);
    Assert.Contains("USB Ports", markup);
  }

  [Fact]
  public void DeviceManagementPage_Structure_Is_Valid()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    var markup = cut.Markup;
    Assert.Contains("mud-tabs", markup);
    Assert.Contains("display: flex", markup);
  }

  [Fact]
  public void DeviceManagementPage_ShowsDisplaySettingsSection()
  {
    var cut = RenderComponent<DeviceManagementPage>();

    Assert.Contains("Device Display Settings", cut.Markup);
  }
}
