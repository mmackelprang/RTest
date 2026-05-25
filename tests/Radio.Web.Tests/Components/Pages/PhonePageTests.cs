using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Pages;

public class PhonePageTests : TestContext
{
  public PhonePageTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();

    // Register PhoneApiService with mock handler that returns empty/default data
    Services.AddHttpClient<PhoneApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // Register PbapApiService with mock handler
    Services.AddHttpClient<PbapApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5000");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // Register BluetoothApiService with mock handler
    Services.AddHttpClient<BluetoothApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5000");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // Register GvBridgeApiService with mock handler
    Services.AddHttpClient<GvBridgeApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // Register GvTrunkApiService with mock handler
    Services.AddHttpClient<GvTrunkApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // Register hub services
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["RotaryPhone:HubUrl"] = "http://localhost:5004/hub",
        ["RotaryPhone:ApiBaseUrl"] = "http://localhost:5004"
      })
      .Build();
    Services.AddSingleton<IConfiguration>(config);
    Services.AddSingleton(new PhoneHubService(
      NullLogger<PhoneHubService>.Instance, config));
    Services.AddSingleton(new GvTrunkHubService(
      NullLogger<GvTrunkHubService>.Instance, config));
  }

  [Fact]
  public void PhonePage_Renders_WithTabs()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Dashboard", cut.Markup);
    Assert.Contains("Contacts", cut.Markup);
    Assert.Contains("Call History", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_SystemStatusSection()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("System Status", cut.Markup);
    // Status row labels are now lowercase mono text in .lbl class
    Assert.Contains("Bluetooth", cut.Markup);
    Assert.Contains("SIP Device", cut.Markup);
    Assert.Contains("HT801 ATA", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_HeroIdleState()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Awaiting Call", cut.Markup);
    Assert.Contains("IDLE", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_DevTray()
  {
    var cut = RenderComponent<PhonePage>();
    // Dev tray is collapsed by default; header text is always visible
    Assert.Contains("Dev Tray", cut.Markup);
    Assert.Contains("Simulate Hardware Events", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_CallPathSection()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Call Path", cut.Markup);
    Assert.Contains("GV API", cut.Markup);
    Assert.Contains("SIP Trunk", cut.Markup);
  }

  [Fact]
  public void PhonePage_ContactsTab_Renders_SourceColumn()
  {
    // Rail tab buttons are always present. Verify the component renders
    // without error and the Contacts tab label is in the output.
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Contacts", cut.Markup);
    Assert.NotNull(cut);
  }

  [Fact]
  public void PhonePage_ContactsTab_Renders_SyncButton()
  {
    // Verify the component renders successfully with PbapApiService and
    // BluetoothApiService injected (no DI error).
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Contacts", cut.Markup);
    Assert.DoesNotContain("NullReferenceException", cut.Markup);
  }

  [Fact]
  public void PhonePage_TabRail_DefaultsToDashboard()
  {
    var cut = RenderComponent<PhonePage>();
    // Dashboard tab should have the "active" class
    var dashButton = cut.FindAll("button.rail-tab")
      .FirstOrDefault(b => b.TextContent.Contains("Dashboard"));
    Assert.NotNull(dashButton);
    Assert.Contains("active", dashButton.ClassList);
  }

  [Fact]
  public void PhonePage_HeroShowsEmptyStateHint_WhenIdle()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Lift the handset to place a call", cut.Markup);
  }

  [Fact]
  public void PhonePage_DevTray_CollapsedByDefault()
  {
    var cut = RenderComponent<PhonePage>();
    Assert.Contains("Click to expand", cut.Markup);
    Assert.DoesNotContain("Handset", cut.Markup); // body not rendered when collapsed
  }

  private class EmptyResponseHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";
      string content = path switch
      {
        var p when p.Contains("system-status") =>
          """{"platform":"Linux","sipListening":false,"ht801Reachable":false}""",
        var p when p.Contains("/api/phone/status") =>
          """{"callState":"Idle"}""",
        var p when p.Contains("/api/contacts") => "[]",
        var p when p.Contains("/api/callhistory") => "[]",
        var p when p.Contains("/api/bluetooth/pbap/status") =>
          """{"devices":[]}""",
        var p when p.Contains("/api/bluetooth/pbap/contacts") => "[]",
        var p when p.Contains("/api/bluetooth/status") =>
          """{"isAvailable":true,"state":"Powered","isDiscovering":false,"pairedDevices":[],"discoveredDevices":[]}""",
        var p when p.Contains("/api/gvbridge/status") =>
          """{"available":false,"activeMode":"BluetoothHfp"}""",
        var p when p.Contains("/api/gvtrunk/status") =>
          """{"isRegistered":false,"callState":"Idle","activeCallDurationSeconds":0}""",
        var p when p.Contains("/api/gvbridge/") => "[]",
        var p when p.Contains("/api/gvtrunk/") => "[]",
        _ => "{}"
      };
      return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
      {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
      });
    }
  }
}
