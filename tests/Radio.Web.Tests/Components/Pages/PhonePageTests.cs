using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Pages;

public class PhonePageTests : TestContext
{
  /// <summary>
  /// ATA reachability the mock system-status response reports. Read lazily per request,
  /// so a test can flip it before <c>RenderComponent&lt;PhonePage&gt;()</c> to exercise
  /// the bell-failure states (bell-failure handoff §3.3, §3.6).
  /// Defaults to a healthy bell so the baseline fixture is a working phone.
  /// </summary>
  private bool? _ht801Reachable = true;

  public PhonePageTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();

    // Register PhoneApiService with mock handler that returns empty/default data
    Services.AddHttpClient<PhoneApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler(() => _ht801Reachable));

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

    // GV Messages singletons the page now injects. The status service is built
    // via a factory (so resolving IServiceScopeFactory doesn't freeze the bUnit
    // service collection mid-registration) and is NEVER started here — no live
    // poll in tests; EmptyResponseHandler returns {"available":false,...} for
    // /api/gvbridge/status so IsAvailable stays false without throwing.
    Services.AddSingleton<PhoneUnreadState>();
    Services.AddSingleton(sp => new GvBridgeStatusService(
      sp.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<GvBridgeStatusService>.Instance, 10));

    // Bell-failure surfacing — the page publishes each status fetch into the app-wide
    // bell health so the topbar fault badge tracks it. Same factory shape and the same
    // never-started rule as GvBridgeStatusService above: constructing it does not begin
    // a poll, so no live HTTP happens in tests.
    Services.AddSingleton(sp => new BellHealthService(
      sp.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<BellHealthService>.Instance, 15));

    // PR3: the Messages feed hosts PhoneTextsPanel, which injects the flagged
    // send service. Register it (send disabled by default) so the panel renders.
    Services.AddHttpClient<GvBridgeSendService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // Task #6: PhoneMessagesPanel injects the contact-name resolution service.
    Services.AddScoped<ContactResolutionService>();
  }

  [Fact]
  public void PhonePage_Renders_WithTabs()
  {
    var cut = RenderComponent<PhonePage>();
    // Messages is the default rail tab; "More" collapses the legacy tabs.
    Assert.Contains("Messages", cut.Markup);
    Assert.Contains("More", cut.Markup);
    // Legacy labels are hidden until "More" is expanded.
    Assert.DoesNotContain("Dashboard", cut.Markup);

    ExpandMore(cut);
    Assert.Contains("Dashboard", cut.Markup);
    Assert.Contains("Contacts", cut.Markup);
    Assert.Contains("Call History", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_SystemStatusSection()
  {
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);
    Assert.Contains("System Status", cut.Markup);
    // Status row labels are now lowercase mono text in .lbl class
    Assert.Contains("Bluetooth", cut.Markup);
    Assert.Contains("SIP Device", cut.Markup);
    // Bell-failure handoff §3.6 — the ATA row is labelled BELL now; the model number
    // moved into the value column as a diagnostic detail.
    Assert.Contains("Bell", cut.Markup);
    Assert.Contains("HT801", cut.Markup);
    Assert.DoesNotContain("HT801 ATA", cut.Markup);
  }

  // ── Bell-failure surfacing, end to end through the page ────────────────────

  [Fact]
  public void PhonePage_BellRow_ReportsOnline_WhenAtaReachable()
  {
    _ht801Reachable = true;
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);

    var row = BellRow(cut);
    Assert.Contains("green", row.QuerySelector(".phone-pill")!.ClassList);
    Assert.Equal("Online", row.QuerySelector(".phone-pill")!.TextContent.Trim());
  }

  [Fact]
  public void PhonePage_BellRow_ReportsUnknown_WhenReachabilityIsNull()
  {
    // The false-alarm regression, exercised through the real fetch + deserialize path:
    // a JSON null must not become a red "Offline".
    _ht801Reachable = null;
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);

    var pill = BellRow(cut).QuerySelector(".phone-pill")!;
    Assert.Contains("gray", pill.ClassList);
    Assert.Equal("Unknown", pill.TextContent.Trim());
    Assert.DoesNotContain("red", pill.ClassList);
  }

  [Fact]
  public void PhonePage_Hero_ShowsDegradedStrip_WhenAtaUnreachable()
  {
    // Proves the whole chain: PhoneApiService -> PhonePage -> PhoneDashboardPanel
    // -> PhoneStatusHero, and that the now-false "wait for an incoming ring" copy is
    // replaced rather than merely supplemented.
    _ht801Reachable = false;
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);

    // Assert on rendered text, not raw markup — the apostrophe is HTML-escaped there.
    Assert.Contains("The phone can't ring right now",
      cut.Find(".phone-hero-alert").TextContent);
    Assert.DoesNotContain("wait for an incoming ring", cut.Markup);
    Assert.Contains("red", BellRow(cut).QuerySelector(".phone-pill")!.ClassList);
  }

  [Fact]
  public void PhonePage_Hero_ShowsNoStrip_WhenReachabilityIsNull()
  {
    // §7m — never alarm on absence of evidence.
    _ht801Reachable = null;
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);

    Assert.Empty(cut.FindAll(".phone-hero-alert"));
    Assert.Contains("wait for an incoming ring", cut.Markup);
  }

  private static AngleSharp.Dom.IElement BellRow(IRenderedComponent<PhonePage> cut) =>
    cut.FindAll(".phone-status-row")
       .Single(r => r.QuerySelector(".lbl")!.TextContent.Trim() == "Bell");

  [Fact]
  public void PhonePage_Renders_HeroIdleState()
  {
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);
    Assert.Contains("Awaiting Call", cut.Markup);
    Assert.Contains("IDLE", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_DevTray()
  {
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);
    // Dev tray is collapsed by default; header text is always visible
    Assert.Contains("Dev Tray", cut.Markup);
    Assert.Contains("Simulate Hardware Events", cut.Markup);
  }

  [Fact]
  public void PhonePage_Renders_CallPathSection()
  {
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);
    Assert.Contains("Call Path", cut.Markup);
    Assert.Contains("GV API", cut.Markup);
    Assert.Contains("SIP Trunk", cut.Markup);
  }

  [Fact]
  public void PhonePage_ContactsTab_Renders_SourceColumn()
  {
    // Rail tab buttons are always present once More is expanded. Verify the
    // component renders without error and the Contacts tab label appears.
    var cut = RenderComponent<PhonePage>();
    ExpandMore(cut);
    Assert.Contains("Contacts", cut.Markup);
    Assert.NotNull(cut);
  }

  [Fact]
  public void PhonePage_ContactsTab_Renders_SyncButton()
  {
    // Verify the component renders successfully with PbapApiService and
    // BluetoothApiService injected (no DI error).
    var cut = RenderComponent<PhonePage>();
    ExpandMore(cut);
    Assert.Contains("Contacts", cut.Markup);
    Assert.DoesNotContain("NullReferenceException", cut.Markup);
  }

  [Fact]
  public void PhonePage_TabRail_DefaultsToMessages()
  {
    var cut = RenderComponent<PhonePage>();
    // Messages tab should have the "active" class by default.
    var messagesButton = cut.FindAll("button.phone-rail-tab")
      .FirstOrDefault(b => b.TextContent.Contains("Messages"));
    Assert.NotNull(messagesButton);
    Assert.Contains("active", messagesButton.ClassList);
  }

  [Fact]
  public void PhonePage_HeroShowsEmptyStateHint_WhenIdle()
  {
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);
    Assert.Contains("Lift the handset to place a call", cut.Markup);
  }

  [Fact]
  public void PhonePage_DevTray_CollapsedByDefault()
  {
    var cut = RenderComponent<PhonePage>();
    OpenDashboard(cut);
    Assert.Contains("Click to expand", cut.Markup);
    Assert.DoesNotContain("Handset", cut.Markup); // body not rendered when collapsed
  }

  // Expand the "More ▸" rail so the legacy tab buttons render.
  private static void ExpandMore(IRenderedComponent<PhonePage> cut)
  {
    var moreButton = cut.FindAll("button.phone-rail-tab")
      .First(b => b.TextContent.Contains("More"));
    moreButton.Click();
  }

  // Switch the page to the legacy Dashboard tab (expand More first).
  private static void OpenDashboard(IRenderedComponent<PhonePage> cut)
  {
    ExpandMore(cut);
    var dashButton = cut.FindAll("button.phone-rail-tab")
      .First(b => b.TextContent.Contains("Dashboard"));
    dashButton.Click();
  }

  private class EmptyResponseHandler : HttpMessageHandler
  {
    // Resolved per request so a test can change the reported reachability after the
    // handler has already been constructed by the HttpClient factory.
    private readonly Func<bool?>? _ht801Reachable;

    public EmptyResponseHandler(Func<bool?>? ht801Reachable = null)
    {
      _ht801Reachable = ht801Reachable;
    }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";
      // null must serialize as a JSON null (not be omitted) so the "not yet probed"
      // case is exercised exactly as RotaryPhone would send it.
      var reachable = (_ht801Reachable?.Invoke()) switch
      {
        true => "true",
        false => "false",
        _ => "null",
      };
      string content = path switch
      {
        var p when p.Contains("system-status") =>
          $$"""{"platform":"Linux","sipListening":false,"ht801IpAddress":"192.168.1.57","ht801Reachable":{{reachable}}}""",
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
