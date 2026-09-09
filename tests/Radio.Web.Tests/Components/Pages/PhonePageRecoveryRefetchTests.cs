using System.Net;
using System.Text;
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
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// GV-12 regression gate. On 2026-09-08 RotaryPhone's GV bridge was dead 14:08–15:31 EDT.
/// After it recovered, radio-web made ZERO further GV calls and the phone surface sat on
/// "Couldn't load…" against a fully healthy backend until a human tapped Retry — because the
/// panels fetch on mount and never again, and a Blazor circuit that survives the outage never
/// re-mounts.
///
/// ⚠ MUST FAIL AGAINST main@56872b55. Today nothing refetches at all, so
/// Recovery_RefetchesThreads_WithoutUserInput asserting a second request is the assertion that
/// was missing. A version of this test that passes on main is testing the wrong thing.
/// </summary>
public class PhonePageRecoveryRefetchTests : TestContext
{
  private const string ThreadId = "thread-1";
  private const string ContactName = "Recovery Contact";

  private readonly RecoveringGvHandler _gv = new();

  public PhonePageRecoveryRefetchTests()
  {
    Services.AddHermeticTestRig();
    JSInterop.Mode = JSRuntimeMode.Loose;
    Services.AddRadzenComponents();

    Services.AddHttpClient<PhoneApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.PhoneApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => new QuietHandler());

    Services.AddHttpClient<PbapApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => new QuietHandler());

    Services.AddHttpClient<BluetoothApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => new QuietHandler());

    // One shared instance so the test can read its counters after the render.
    Services.AddHttpClient<GvBridgeApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.PhoneApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => _gv);

    Services.AddSingleton<GvMarkReadDarkLatch>();

    Services.AddHttpClient<GvTrunkApiService>(client =>
    {
      client.BaseAddress = new Uri(HermeticTestRig.PhoneApiBaseUrl);
    }).ConfigurePrimaryHttpMessageHandler(() => new QuietHandler());

    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["RotaryPhone:HubUrl"] = $"{HermeticTestRig.PhoneApiBaseUrl}/hub",
        ["RotaryPhone:ApiBaseUrl"] = HermeticTestRig.PhoneApiBaseUrl
      })
      .Build();
    Services.AddSingleton<IConfiguration>(config);
    Services.AddSingleton(new PhoneHubService(
      NullLogger<PhoneHubService>.Instance, config, new OfflineHubTransport()));
    Services.AddSingleton(new GvTrunkHubService(
      NullLogger<GvTrunkHubService>.Instance, config, new OfflineHubTransport()));

    Services.AddSingleton<PhoneUnreadState>();
    // Registered, never started — the test drives ApplyStatusForTest instead of a live poll.
    Services.AddSingleton(sp => new GvBridgeStatusService(
      sp.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<GvBridgeStatusService>.Instance, 10));
    Services.AddSingleton(sp => new BellHealthService(
      sp.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<BellHealthService>.Instance, 15));

    Services.AddScoped<ContactResolutionService>();
  }

  private static GvBridgeStatusDto Unhealthy() =>
    new() { Available = false, ActiveMode = "BluetoothHfp" };

  private static GvBridgeStatusDto Healthy() =>
    new() { Available = true, ActiveMode = "GoogleVoice" };

  [Fact]
  public void Recovery_RefetchesThreads_WithoutUserInput()
  {
    _gv.FailThreadList = true;

    var cut = RenderComponent<PhonePage>();
    var status = Services.GetRequiredService<GvBridgeStatusService>();

    // Mount fetch happened and failed — this is 15:31:16, the panel showing "Couldn't load…".
    cut.WaitForAssertion(
      () => Assert.Equal(1, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(5));

    // Seed the unhealthy side of the edge. No wait here: WaitForAssertion returns the moment
    // the assertion passes, and the count is already 1, so an immediate `Assert.Equal(1, …)`
    // would return before an erroneous first-delivery refetch could land — it would assert
    // nothing. The exact-count check after the recovery is what makes this testable.
    status.ApplyStatusForTest(Unhealthy());

    // 15:31:17 — the bridge comes back. Nothing else happens. No click, no keystroke.
    _gv.FailThreadList = false;
    status.ApplyStatusForTest(Healthy());

    // ⚠ THE ASSERTION THE ROW EXISTS FOR. Measured RED on fb3cba9d: without the trigger this
    // stays at 1 forever and the recovered content never appears.
    cut.WaitForAssertion(
      () => Assert.Contains(ContactName, cut.Markup),
      timeout: TimeSpan.FromSeconds(5));

    // ⚠ EXACTLY 2 — one mount fetch, one recovery fetch — and the exactness is the point: it
    // pins the recovery to ONE fetch, so a trigger that fired on every delivery rather than on
    // the edge would overshoot and fail here.
    //
    // What it does NOT prove is the first-delivery property, and the earlier claim that "a
    // spurious refetch on the unhealthy delivery would make this 3" is not guaranteed: a
    // refetch on the first delivery would set _gvRefetching, the healthy delivery would then
    // return early on that guard, and the total would land on 2 anyway. The first-delivery
    // property is instead guaranteed by construction — _gvHealthyLast starts null, and
    // `null == false` is false in C#, so `_gvHealthyLast == false && healthy` cannot be true on
    // the first status the page ever sees. That matters because the mount fetch already ran;
    // refetching there would double every page open.
    Assert.Equal(2, _gv.ThreadListCalls);
  }

  [Fact]
  public void RepeatedHealthyStatus_DoesNotRefetchAgain()
  {
    // Only the EDGE refetches. A healthy poll every 10 s must not become a fetch every 10 s —
    // that is the backoff loop this row deliberately did not build.
    var cut = RenderComponent<PhonePage>();
    var status = Services.GetRequiredService<GvBridgeStatusService>();

    cut.WaitForAssertion(
      () => Assert.Equal(1, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(5));

    status.ApplyStatusForTest(Unhealthy());
    status.ApplyStatusForTest(Healthy());

    cut.WaitForAssertion(
      () => Assert.Equal(2, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(5));

    for (var i = 0; i < 5; i++)
    {
      status.ApplyStatusForTest(Healthy());
    }

    // ⚠ DO NOT "SIMPLIFY" THIS BACK TO A WaitForAssertion(() => Assert.Equal(2, …)) HERE.
    // WaitForAssertion returns the instant its assertion first passes, and the wait above has
    // already established the count is 2 — so that call would return on its first evaluation,
    // before any of the five deliveries could possibly have landed a fetch. It asserted
    // nothing, and the test passed against an implementation with the edge check removed
    // (`var recovered = healthy;`), which is the one thing it exists to prevent.
    //
    // Driving one MORE real edge is what makes the five in between observable. The second edge
    // is guaranteed to refetch, so the count must reach exactly 3: every one of the five
    // repeated healthy deliveries contributed nothing. Had any of them fetched, this would be
    // 4 or more and the plain Assert below fails.
    status.ApplyStatusForTest(Unhealthy());
    status.ApplyStatusForTest(Healthy());

    cut.WaitForAssertion(
      () => Assert.Equal(3, _gv.ThreadListCalls),
      timeout: TimeSpan.FromSeconds(5));
    Assert.Equal(3, _gv.ThreadListCalls);
  }

  /// <summary>
  /// Serves the GV routes, counts the SMS thread-list requests, and can be flipped from
  /// failing to succeeding mid-test — the row's "fail N times, then succeed".
  /// </summary>
  private class RecoveringGvHandler : HttpMessageHandler
  {
    private int _threadListCalls;

    public volatile bool FailThreadList;
    public int ThreadListCalls => Volatile.Read(ref _threadListCalls);

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";

      // The two SMS routes differ only by the trailing segment, so the bodies route must be
      // matched FIRST — same ordering note as PhonePageThreadLoadErrorTests.
      if (path.Contains("/sms/threads/"))
      {
        return Json(HttpStatusCode.OK,
          $$"""{"threadId":"{{ThreadId}}","messages":[],"fetchedAtUtc":"2026-09-08T19:31:17Z"}""");
      }

      if (path.Contains("/sms/threads?"))
      {
        Interlocked.Increment(ref _threadListCalls);
        if (FailThreadList)
        {
          return Json(HttpStatusCode.BadGateway, """{"error":"upstream_error"}""");
        }
        return Json(HttpStatusCode.OK, $$"""
          {"threads":[{"threadId":"{{ThreadId}}","counterpartyNumber":"+15551234567",
          "counterpartyName":"{{ContactName}}","lastMessageAt":"2026-09-08T19:00:00Z",
          "hasUnread":false,"lastMessagePreview":"Hello"}],
          "fetchedAtUtc":"2026-09-08T19:31:17Z"}
          """);
      }

      if (path.Contains("/api/gvbridge/voicemail"))
      {
        return Json(HttpStatusCode.OK,
          """{"items":[],"nextPageToken":null,"fetchedAtUtc":"2026-09-08T19:31:17Z"}""");
      }

      if (path.Contains("/api/gvbridge/status"))
      {
        return Json(HttpStatusCode.OK, """{"available":true,"activeMode":"GoogleVoice"}""");
      }

      return Json(HttpStatusCode.OK, path.Contains("/api/gvbridge/") ? "[]" : "{}");
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode code, string body) =>
      Task.FromResult(new HttpResponseMessage(code)
      {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
      });
  }

  /// <summary>Adapted from PhonePageThreadLoadErrorTests.EmptyResponseHandler.</summary>
  private class QuietHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var path = request.RequestUri?.PathAndQuery ?? "";
      string content = path switch
      {
        var p when p.Contains("system-status") =>
          """{"platform":"Linux","sipListening":false,"ht801IpAddress":"192.168.1.57","ht801Reachable":true}""",
        var p when p.Contains("/api/phone/status") => """{"callState":"Idle"}""",
        var p when p.Contains("/api/contacts") => "[]",
        var p when p.Contains("/api/callhistory") => "[]",
        var p when p.Contains("/api/bluetooth/pbap/status") => """{"devices":[]}""",
        var p when p.Contains("/api/bluetooth/pbap/contacts") => "[]",
        var p when p.Contains("/api/bluetooth/status") =>
          """{"isAvailable":true,"state":"Powered","isDiscovering":false,"pairedDevices":[],"discoveredDevices":[]}""",
        var p when p.Contains("/api/gvtrunk/status") =>
          """{"isRegistered":false,"callState":"Idle","activeCallDurationSeconds":0}""",
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
