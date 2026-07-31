using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// GV-8 / UAT F-1 end-to-end regression gate. The rest of the suite covers only the two
/// ENDS of the chain: <c>GvBridgeApiService</c> returning a typed outcome instead of a
/// bare null, and <c>PhoneTextsPanel</c> rendering its error branch when its
/// Loading/Error parameters are set directly. Nothing exercised the two links in
/// between — <c>PhonePage</c> coalescing its own load state into
/// <c>PhoneMessagesPanel</c>'s OpenThreadLoading/OpenThreadError parameters, and
/// <c>PhoneMessagesPanel</c> forwarding those into <c>PhoneTextsPanel</c>'s Loading/Error
/// — so a future edit that silently drops one of those parameter bindings would
/// reintroduce F-1 (a failed load rendering as an empty conversation) with nothing in
/// the suite catching it. This test drives the real component tree — click a thread row,
/// let the mocked 502 land — and asserts the error copy renders instead of the empty-
/// conversation copy.
/// </summary>
public class PhonePageThreadLoadErrorTests : TestContext
{
  private const string ThreadId = "thread-1";
  private const string ContactName = "Test Contact";

  public PhonePageThreadLoadErrorTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();

    // Register PhoneApiService with a plain empty-response handler (bell/call-history
    // state is not under test here).
    Services.AddHttpClient<PhoneApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    Services.AddHttpClient<PbapApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5000");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    Services.AddHttpClient<BluetoothApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5000");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    // The handler under test: one unread thread on the list route, HTTP 502 on that
    // thread's bodies route — GV-8 / UAT F-1's exact failure shape.
    Services.AddHttpClient<GvBridgeApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new ThreadLoadErrorHandler());

    Services.AddHttpClient<GvTrunkApiService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

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

    // Never started here — no live poll in tests, matching PhonePageTests.
    Services.AddSingleton<PhoneUnreadState>();
    Services.AddSingleton(sp => new GvBridgeStatusService(
      sp.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<GvBridgeStatusService>.Instance, 10));
    Services.AddSingleton(sp => new BellHealthService(
      sp.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<BellHealthService>.Instance, 15));

    Services.AddHttpClient<GvBridgeSendService>(client =>
    {
      client.BaseAddress = new Uri("http://localhost:5004");
    }).ConfigurePrimaryHttpMessageHandler(() => new EmptyResponseHandler());

    Services.AddScoped<ContactResolutionService>();
  }

  [Fact]
  public void PhonePage_OpenThreadFailure_ShowsErrorState_NotEmptyConversation()
  {
    var cut = RenderComponent<PhonePage>();

    // The thread list fetch is fired-and-forgotten from OnInitializedAsync; wait for the
    // row to land before trying to click it.
    cut.WaitForAssertion(
      () => Assert.Contains(ContactName, cut.Markup),
      timeout: TimeSpan.FromSeconds(5));

    var threadRow = cut.FindAll("button.list-item-touch")
      .Single(b => b.TextContent.Contains(ContactName));
    threadRow.Click();

    // The bodies fetch resolves the mocked 502 asynchronously; wait for the pane to
    // settle into its error branch rather than sleeping.
    cut.WaitForAssertion(
      () => Assert.Contains("Couldn't load messages.", cut.Markup),
      timeout: TimeSpan.FromSeconds(5));

    // The old bug: a 502 rendered byte-identical to a genuinely empty conversation.
    // This is the one assertion that would have caught it.
    Assert.DoesNotContain("Start the conversation below.", cut.Markup);
  }

  // Returns a thread list with one unread thread for the list route, HTTP 502 for that
  // thread's bodies route, and otherwise behaves like PhonePageTests's
  // EmptyResponseHandler. The two GV SMS routes differ only by the trailing path
  // segment (".../sms/threads?..." vs ".../sms/threads/{id}?..."), so the bodies route
  // is matched FIRST on the trailing '/' before the list route's '?' match.
  private class ThreadLoadErrorHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";

      if (path.Contains("/sms/threads/"))
      {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
          Content = new StringContent("""{"error":"upstream_error"}""",
            Encoding.UTF8, "application/json")
        });
      }

      if (path.Contains("/sms/threads?"))
      {
        var body = $$"""
          {"threads":[{"threadId":"{{ThreadId}}","counterpartyNumber":"+15551234567",
          "counterpartyName":"{{ContactName}}","lastMessageAt":"2026-07-30T12:00:00Z",
          "hasUnread":true,"lastMessagePreview":"Hello"}],
          "fetchedAtUtc":"2026-07-30T12:00:00Z"}
          """;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
      }

      string content = path switch
      {
        var p when p.Contains("/api/gvbridge/status") =>
          """{"available":false,"activeMode":"BluetoothHfp"}""",
        var p when p.Contains("/api/gvbridge/") => "[]",
        _ => "{}"
      };
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
      });
    }
  }

  // Adapted from PhonePageTests's EmptyResponseHandler (the ht801Reachable knob is
  // dropped — bell state is not under test here, so a fixed healthy reading is enough).
  private class EmptyResponseHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";
      string content = path switch
      {
        var p when p.Contains("system-status") =>
          """{"platform":"Linux","sipListening":false,"ht801IpAddress":"192.168.1.57","ht801Reachable":true}""",
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
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
      });
    }
  }
}
