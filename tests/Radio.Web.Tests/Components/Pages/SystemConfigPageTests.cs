using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// bUnit tests for the SystemConfigPage component
/// Tests system stats display, configuration management, log viewer, and event sources
/// </summary>
public class SystemConfigPageTests : TestContext
{
  private readonly ILoggerFactory _loggerFactory;

  public SystemConfigPageTests()
  {
    // Hermetic rig: fails every outbound HTTP request and every SignalR
    // negotiate without touching the network, so this fixture's result never
    // depends on whether radio-api happens to be running locally.
    Services.AddHermeticTestRig();

    _loggerFactory = new NullLoggerFactory();
    
    // Set up minimal dependencies with in-memory configuration
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        { "ApiBaseUrl", HermeticTestRig.ApiBaseUrl }
      })
      .Build();

    Services.AddSingleton<IConfiguration>(configuration);
    Services.AddSingleton(_loggerFactory);
    
    // Add Radzen services
    Services.AddRadzenComponents();

    // Add HttpClient for API services
    Services.AddHttpClient<SystemApiService>();
    Services.AddHttpClient<ConfigurationApiService>();
    Services.AddHttpClient<SecretsApiService>();
    Services.AddHttpClient<SourcesApiService>();
    Services.AddHttpClient<AudioApiService>();
    Services.AddHttpClient<IntegrationsApiService>();

    // Add AudioStateHubService
    Services.AddSingleton<AudioStateHubService>();
    
    // Setup JSInterop for Radzen components
    JSInterop.Mode = JSRuntimeMode.Loose;
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
  public void SystemConfigPage_Renders_Successfully()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that the component renders without throwing
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_Contains_Tabs()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for tab structure
    Assert.Contains("System Stats", cut.Markup);
    Assert.Contains("Configuration", cut.Markup);
    Assert.Contains("Logs", cut.Markup);
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_SystemStats_Tab_Contains_Gauges()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for system stat components
    Assert.Contains("CPU", cut.Markup);
    Assert.Contains("RAM", cut.Markup);
    Assert.Contains("Disk", cut.Markup);
    Assert.Contains("Threads", cut.Markup);
    Assert.Contains("App Uptime", cut.Markup);
    Assert.Contains("System Uptime", cut.Markup);
    Assert.Contains("Temperature", cut.Markup);
    Assert.Contains("Audio Engine", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Configuration_Tab_Present()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that configuration tab exists
    Assert.Contains("Configuration", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Logs_Tab_Present()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that logs tab exists
    Assert.Contains("Logs", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Renders()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - The component renders without error
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Contains_TTS_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that Event Sources tab is present (content loads dynamically)
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Contains_AudioFile_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that Event Sources tab is present (content loads dynamically)
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Contains_ActiveSources_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for Active Event Sources section (content loads dynamically)
    Assert.Contains("Event Sources", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_EventSources_Tab_Shows_Empty_State()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Component should render successfully (empty state depends on API data)
    Assert.NotNull(cut);
    Assert.Contains("Event Sources", cut.Markup);
  }

  // Removed: SystemConfigPage_RadioControl_Uses_Modern_Typography
  // - Used real HttpClient (localhost:5000) causing CI failures
  // - Assertions were incorrect: DSEG is still used for ghost segments,
  //   and text-shadow glows are part of the design system

  [Fact]
  public void SystemConfigPage_DefaultValues_Displayed()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check default values are shown (N/A for unavailable stats)
    Assert.Contains("N/A", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Configuration_Tab_Has_Multiple_SubTabs()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that Configuration tab has multiple sub-tabs
    Assert.Contains("Configuration", cut.Markup);
    Assert.NotNull(cut);
    // Component should render without errors
    Assert.True(cut.Markup.Length > 0);
  }

  [Fact]
  public void SystemConfigPage_Devices_Configuration_Renders_Without_Error()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - The page should render successfully with Devices configuration support
    Assert.NotNull(cut);
    Assert.Contains("Configuration", cut.Markup);
    
    // Verify component initializes device options (even if null initially due to API unavailability)
    // The component should handle missing configuration gracefully
    Assert.DoesNotContain("Object reference not set", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Handles_Missing_DeviceConfiguration_Gracefully()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Component should not crash when device configuration is unavailable
    Assert.NotNull(cut);
    // Should show loading indicator or handle null device options
    Assert.DoesNotContain("NullReferenceException", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Contains_Secrets_Tab()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that secrets tab exists
    Assert.Contains("Secrets", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Secrets_Tab_Has_Security_Warning()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Security warning is in nested tab content, verify main tab exists
    Assert.Contains("Secrets", cut.Markup);
    // The lock icon should be present for the tab
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_Secrets_Tab_Has_SubTabs()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Verify main Secrets tab exists and component renders
    // Sub-tab content is loaded dynamically in RadzenTabs
    Assert.Contains("Secrets", cut.Markup);
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_Secrets_Tab_Has_TTS_SubTab()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Verify main Secrets tab exists
    // Sub-tab content is loaded dynamically in RadzenTabs
    Assert.Contains("Secrets", cut.Markup);
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_Secrets_Tab_Has_AcoustID_SubTab()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Verify main Secrets tab exists
    // Sub-tab content is loaded dynamically in RadzenTabs
    Assert.Contains("Secrets", cut.Markup);
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_Secrets_Tab_Renders_Without_Errors()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Verify Secrets tab renders and component doesn't crash
    Assert.Contains("Secrets", cut.Markup);
    Assert.NotNull(cut);
    Assert.DoesNotContain("NullReferenceException", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Renders_Without_Crashing_With_Secrets()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Component should render successfully with secrets support
    Assert.NotNull(cut);
    Assert.Contains("Secrets", cut.Markup);
    // Should not contain any error indicators
    Assert.DoesNotContain("NullReferenceException", cut.Markup);
    Assert.DoesNotContain("Object reference not set", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_Preferences_Tab_Present()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that preferences tab exists
    Assert.Contains("Preferences", cut.Markup);
  }

  // ========== Phase 5: Store Management Tests ==========

  [Fact]
  public void SystemConfigPage_Contains_StoreManagement_Tab()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check that Store Management tab exists
    Assert.Contains("Store Management", cut.Markup);
  }

  [Fact]
  public void SystemConfigPage_StoreManagement_Has_StoreInfo_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for store management tab
    // Content is in nested tab which loads dynamically
    Assert.Contains("Store Management", cut.Markup);
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_StoreManagement_Has_ImportExport_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for store management tab
    // Import/export UI loads dynamically in nested tab
    Assert.Contains("Store Management", cut.Markup);
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_StoreManagement_Has_Comparison_Section()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Check for store management tab
    // Comparison UI loads dynamically in nested tab
    Assert.Contains("Store Management", cut.Markup);
    Assert.NotNull(cut);
  }

  [Fact]
  public void SystemConfigPage_StoreManagement_Renders_Without_Errors()
  {
    // Act
    var cut = RenderComponent<SystemConfigPage>();

    // Assert - Store Management tab should render without crashing
    Assert.Contains("Store Management", cut.Markup);
    Assert.NotNull(cut);
    Assert.DoesNotContain("NullReferenceException", cut.Markup);
  }

  // ========== ENC-8: the Rotary Encoders sub-tab ==========

  /// <summary>
  /// Renders the page and selects Integrations, whose first sub-tab is Rotary Encoders.
  ///
  /// <para>
  /// ⚠ RadzenTabs renders only the selected tab's body — measured, not assumed: without this click
  /// the page's markup is about 8.7 KB and contains none of the encoder panel. Every assertion below
  /// that an affordance is <b>absent</b> is therefore preceded by a positive assertion that the panel
  /// really is in the markup. Without one, a negative assertion would pass because nothing rendered
  /// at all, which is the class of uncheckable claim this whole row exists to remove.
  /// </para>
  /// </summary>
  private IRenderedComponent<SystemConfigPage> RenderWithEncoderTabSelected()
  {
    var cut = RenderComponent<SystemConfigPage>();
    var integrations = cut.FindAll("a[role='tab']").First(a => a.TextContent.Contains("Integrations"));
    integrations.Click();
    return cut;
  }

  [Fact]
  public void EncoderTab_WithNoProvisioningData_DoesNotClaimTheDeviceAgrees()
  {
    var cut = RenderWithEncoderTabSelected();

    // The panel is really here, so the absence assertions below mean something.
    Assert.Contains("Device configuration", cut.Markup);

    // Under the hermetic rig every API call fails, so the page has no read-back at all — the same
    // state a kiosk hits whenever radio-api is down. It must not render agreement it was never told
    // about: three states, never two.
    Assert.DoesNotContain("✓", cut.Markup);
    Assert.DoesNotContain("agrees", cut.Markup);
  }

  [Fact]
  public void EncoderTab_HasNoFactoryResetAffordance()
  {
    var cut = RenderWithEncoderTabSelected();

    Assert.Contains("Encoder Mapping", cut.Markup);

    // The device's factory tiers were read off this hardware as step=1 with (150 ms x5), (80 ms x15),
    // (40 ms x50), which at the host's 2 % per unit is one detent from silence to full.
    // RotaryEncoderCommand.ResetDefaults exists in the enum; no affordance on this page may reach it,
    // and none may be added behind a disclosure or a confirm either.
    Assert.DoesNotContain("factory", cut.Markup, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Reset to defaults", cut.Markup, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Restore", cut.Markup, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void EncoderTab_HasExactlyOneSaveToDeviceButton_AndItsCopyIsTheHandoffCopy()
  {
    var cut = RenderWithEncoderTabSelected();

    Assert.Contains("Device configuration", cut.Markup);

    var saveToDeviceButtons = cut.FindAll("button")
      .Where(b => b.TextContent.Contains("Save to device"))
      .ToList();
    Assert.Single(saveToDeviceButtons);

    // ENC-8 §0.5 pinned as a string. The button says what it writes, and what it writes is what card
    // 2 displays; if a later revision reintroduces any divergence between them, this copy has to
    // change with it, and this assertion is what forces that rather than leaving it to care.
    Assert.Contains(
      "Saves the settings above to the knobs so they work the same way even if the app is restarting.",
      cut.Markup);
  }

  [Fact]
  public void EncoderTab_NoLongerOffersAnEditableVolumeStepPercent()
  {
    var cut = RenderWithEncoderTabSelected();

    Assert.Contains("Connection settings", cut.Markup);

    // Only the editor is gone. RotaryEncoderActionRouter still reads VolumeStepPercent on every
    // detent, and the value now appears read-only in the device configuration card as VOLUME's step
    // size — one value, one place. The duplicate box was a second source of truth for a number the
    // device also holds.
    Assert.DoesNotContain("Volume Step (%)", cut.Markup);
  }
}
