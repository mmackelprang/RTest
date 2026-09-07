using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

public class GvBridgeApiServiceVoicemailSmsTests
{
  private static readonly JsonSerializerOptions JsonOptions =
    new() { PropertyNameCaseInsensitive = true };

  private static GvBridgeApiService CreateService(HttpClient client) =>
    new(client, NullLogger<GvBridgeApiService>.Instance,
      new ConfigurationBuilder().Build(), new GvMarkReadDarkLatch());

  // GV-4: mark-read routes are gated on RotaryPhone:Gv:MarkReadEnabled; this builds
  // a service with that flag set so the flag-on/flag-off paths are both exercised.
  // GV-6: `latch` and `logger` default to FRESH instances so every existing case stays isolated
  // — a shared default would let one test's latch decide another test's outcome and make the
  // class order-dependent. Pass them explicitly only when the case is about sharing or logging.
  private static GvBridgeApiService BuildSvc(MockHttpHandler handler, bool markReadEnabled,
    GvMarkReadDarkLatch? latch = null, ILogger<GvBridgeApiService>? logger = null)
  {
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:MarkReadEnabled"] = markReadEnabled.ToString() })
      .Build();
    return new GvBridgeApiService(client, logger ?? NullLogger<GvBridgeApiService>.Instance,
      config, latch ?? new GvMarkReadDarkLatch());
  }

  [Fact]
  public async Task GetVoicemailsAsync_ReturnsList()
  {
    var dto = new VoicemailListDto(
      new[]
      {
        new VoicemailItemDto("vm1", "t1", "+15551234567", "Jane",
          DateTime.UtcNow, 42, false, "hi", "/api/gvbridge/voicemail/vm1/audio")
      },
      null, DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetVoicemailsAsync();

    Assert.NotNull(result);
    Assert.Single(result!.Items);
    Assert.Equal("vm1", result.Items[0].Id);
  }

  [Fact]
  public async Task GetVoicemailsAsync_ReturnsNull_OnError()
  {
    var handler = new MockHttpHandler(statusCode: System.Net.HttpStatusCode.InternalServerError);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetVoicemailsAsync();

    Assert.Null(result);
  }

  [Fact]
  public async Task GetSmsThreadsAsync_ReturnsThreads()
  {
    var dto = new SmsThreadListDto(
      new[] { new SmsThreadDto("t1", "+15551234567", "Mom",
        DateTime.UtcNow, true, "Did you eat?") },
      DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadsAsync();

    Assert.NotNull(result);
    Assert.Single(result!.Threads);
    Assert.True(result.Threads[0].HasUnread);
  }

  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsSuccess_WithMessages()
  {
    var dto = new SmsThreadMessagesDto("t1",
      new[] { new SmsMessageDto("m1", "t1", "Inbound", "+15551234567",
        "hello", DateTime.UtcNow, false) },
      DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.True(result.IsSuccess);
    Assert.Equal(GvCallOutcome.Success, result.Outcome);
    Assert.NotNull(result.Value);
    Assert.Single(result.Value!.Messages);
  }

  // THE test gap the GV-8 queue row calls out. This is the exact live failure: during
  // RotaryPhone's ~9-minute GV auth blackout the bridge returns 502, which used to
  // collapse to null and render as an empty conversation (UAT F-1).
  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsHttpError_OnNon2xx()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.BadGateway);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.False(result.IsSuccess);
    Assert.True(result.IsFailure);
    Assert.Equal(GvCallOutcome.HttpError, result.Outcome);
    Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    Assert.Null(result.Value);
  }

  // The error discriminator is what GV-6 will branch on for 409 markread_disabled;
  // here it is captured so the operator-facing log line names the upstream failure.
  [Fact]
  public async Task GetSmsThreadMessagesAsync_CapturesErrorCode_FromFailureBody()
  {
    var handler = new MockHttpHandler("{\"error\":\"upstream_error\"}",
      HttpStatusCode.BadGateway);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.Equal(GvCallOutcome.HttpError, result.Outcome);
    Assert.Equal("upstream_error", result.ErrorCode);
  }

  // A 200 whose body is not the DTO (RotaryPhone's SPA fallback serves index.html with
  // HTTP 200 — see F-1-DIAGNOSIS § Defect B) must be a failure, never an empty thread.
  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsMalformed_OnUndeserializableBody()
  {
    var handler = new MockHttpHandler("<html><body>not json</body></html>");
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.False(result.IsSuccess);
    Assert.Equal(GvCallOutcome.Malformed, result.Outcome);
    Assert.Null(result.Value);
  }

  // GV-8 L-1: Messages is declared non-nullable on SmsThreadMessagesDto, but STJ
  // doesn't enforce that at deserialize time — a 2xx body that omits "messages"
  // must not reach PhonePage's `.Messages.ToList()`, which would throw inside a
  // Blazor event handler and tear down the circuit (reconnect overlay instead of
  // the error state).
  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsMalformed_WhenMessagesAbsent()
  {
    var handler = new MockHttpHandler(
      "{\"threadId\":\"t1\",\"fetchedAtUtc\":\"2026-06-20T18:03:11Z\"}");
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.False(result.IsSuccess);
    Assert.Equal(GvCallOutcome.Malformed, result.Outcome);
    Assert.Null(result.Value);
  }

  [Fact]
  public async Task MarkVoicemailReadAsync_NoOps_WhenFlagOff()
  {
    var handler = new MockHttpHandler("{}");
    var svc = BuildSvc(handler, markReadEnabled: false);

    var result = await svc.MarkVoicemailReadAsync("vm1");

    Assert.Null(result);                  // no DTO when flag off
    Assert.Equal(0, handler.RequestCount); // never hit the network
  }

  [Fact]
  public async Task MarkVoicemailReadAsync_ReturnsDto_On200_WhenFlagOn()
  {
    // Frozen VoicemailItemDto read shape (ADR-024 §3.1).
    const string body = """
      { "id":"vm1","threadId":"t1","fromNumber":"+15551234567","fromName":"Jane",
        "receivedAt":"2026-06-20T18:03:11Z","durationSeconds":42,"isRead":true,
        "transcript":"hi","audioUrl":"/api/gvbridge/voicemail/vm1/audio" }
      """;
    var handler = new MockHttpHandler(body);   // 200 OK
    var svc = BuildSvc(handler, markReadEnabled: true);

    var dto = await svc.MarkVoicemailReadAsync("vm1");

    Assert.NotNull(dto);
    Assert.True(dto!.IsRead);
    Assert.Equal("vm1", dto.Id);
    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task MarkVoicemailReadAsync_ReturnsNull_On404_WhenFlagOn()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = BuildSvc(handler, markReadEnabled: true);

    Assert.Null(await svc.MarkVoicemailReadAsync("gone"));
  }

  [Fact]
  public async Task MarkVoicemailReadAsync_ReturnsNull_On502_NoRetry_WhenFlagOn()
  {
    // 502 = GV unreachable. Caller keeps the optimistic flip; client never auto-retries.
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.BadGateway);
    var svc = BuildSvc(handler, markReadEnabled: true);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));
    Assert.Equal(1, handler.RequestCount);  // exactly one attempt, no retry
  }

  [Fact]
  public async Task MarkSmsThreadReadAsync_ReturnsDto_On200_WhenFlagOn()
  {
    // Frozen SmsThreadDto read shape (ADR-024 §3.2).
    const string body = """
      { "threadId":"t1","counterpartyNumber":"+15551234567","counterpartyName":"Mom",
        "lastMessageAt":"2026-06-20T18:03:11Z","hasUnread":false,"lastMessagePreview":"ok" }
      """;
    var handler = new MockHttpHandler(body);
    var svc = BuildSvc(handler, markReadEnabled: true);

    var dto = await svc.MarkSmsThreadReadAsync("t1");

    Assert.NotNull(dto);
    Assert.False(dto!.HasUnread);
    Assert.Equal("t1", dto.ThreadId);
    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task MarkSmsThreadReadAsync_NoOps_WhenFlagOff()
  {
    var handler = new MockHttpHandler("{}");
    var svc = BuildSvc(handler, markReadEnabled: false);

    Assert.Null(await svc.MarkSmsThreadReadAsync("t1"));
    Assert.Equal(0, handler.RequestCount);
  }

  // ── GV-6: a dark mark-read feature is not a failure ──────────────

  private const string DarkBody = """{"error":"markread_disabled"}""";

  [Fact]
  public async Task MarkVoicemailReadAsync_Dark409_ReturnsNull_AndSuppressesTheSecondPost()
  {
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));
    Assert.Null(await svc.MarkVoicemailReadAsync("vm2"));

    Assert.True(latch.IsLatched);
    Assert.Equal(1, handler.RequestCount);   // the second call never reached the network
  }

  [Fact]
  public async Task MarkSmsThreadReadAsync_SharesTheLatch_WithVoicemail()
  {
    // One server flag gates both routes (ADR-024 §3.3), so a 409 on either must silence both.
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));   // latches
    Assert.Null(await svc.MarkSmsThreadReadAsync("t1"));    // must not POST

    Assert.Equal(1, handler.RequestCount);
  }

  // ⚠ What this does NOT prove: calls 2 and 3 return at the IsLatched short-circuit and never
  // reach HandledAsMarkReadDark, so the once-ness shown here is the LATCH's, not TryLatch()'s
  // return value's. That guard is covered by
  // Dark409_TwoCircuitsRacingTheFirstPost_StillLogOneWarningTotal below.
  [Fact]
  public async Task Dark409_LogsOneWarning_ThenTheLatchSilencesBothMethods()
  {
    var entries = new List<(LogLevel Level, string Message)>();
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch,
      new CapturingLogger<GvBridgeApiService>(entries));

    await svc.MarkVoicemailReadAsync("vm1");
    await svc.MarkVoicemailReadAsync("vm2");
    await svc.MarkSmsThreadReadAsync("t1");

    // The documented grep anchor — if this substring changes, INTEGRATIONS.md's probe breaks.
    var dark = entries.Where(e => e.Message.Contains("GV mark-read is dark",
      StringComparison.Ordinal)).ToList();
    Assert.Single(dark);
    Assert.Equal(LogLevel.Warning, dark[0].Level);
    // No per-call Error noise once the feature is known to be dark.
    Assert.DoesNotContain(entries, e => e.Level == LogLevel.Error);
  }

  [Fact]
  public async Task Conflict_WithADifferentErrorCode_IsAGenuineFailure_AndDoesNotLatch()
  {
    // ADR-024 §3.3 defines ONE meaning for 409 on these routes. Anything else is unknown, and an
    // unknown failure must not silence a feature the operator asked for.
    var entries = new List<(LogLevel Level, string Message)>();
    var handler = new MockHttpHandler("""{"error":"something_else"}""", HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch,
      new CapturingLogger<GvBridgeApiService>(entries));

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));
    Assert.Null(await svc.MarkVoicemailReadAsync("vm2"));

    Assert.False(latch.IsLatched);
    Assert.Equal(2, handler.RequestCount);           // still trying, correctly
    Assert.Equal(2, entries.Count(e => e.Level == LogLevel.Error));
    Assert.Contains(entries, e => e.Message.Contains("something_else", StringComparison.Ordinal));
  }

  [Fact]
  public async Task BadGateway_DoesNotLatch_AndKeepsReportingEachFailure()
  {
    var entries = new List<(LogLevel Level, string Message)>();
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.BadGateway);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch,
      new CapturingLogger<GvBridgeApiService>(entries));

    Assert.Null(await svc.MarkSmsThreadReadAsync("t1"));
    Assert.Null(await svc.MarkSmsThreadReadAsync("t2"));

    Assert.False(latch.IsLatched);
    Assert.Equal(2, handler.RequestCount);
    Assert.Equal(2, entries.Count(e => e.Level == LogLevel.Error));
    Assert.DoesNotContain(entries,
      e => e.Message.Contains("GV mark-read is dark", StringComparison.Ordinal));
  }

  [Fact]
  public async Task FlagOff_NeverLatches_EvenIfTheServerWouldReject()
  {
    // The two guards are independent: our flag is the reason we do not call, and their 409 is
    // the reason we stop calling. With ours off, theirs is never observed.
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: false, latch);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));

    Assert.False(latch.IsLatched);
    Assert.Equal(0, handler.RequestCount);
  }

  // The mirror of MarkSmsThreadReadAsync_SharesTheLatch_WithVoicemail. Worth its own case because
  // the SMS route has its own ReadErrorCodeAsync + HandledAsMarkReadDark call site, and until this
  // test that site had no direct coverage for the dark 409 — only for being suppressed BY one.
  [Fact]
  public async Task MarkVoicemailReadAsync_SharesTheLatch_WithSms()
  {
    var handler = new MockHttpHandler(DarkBody, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch);

    Assert.Null(await svc.MarkSmsThreadReadAsync("t1"));    // latches, via the SMS route
    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));   // must not POST

    Assert.True(latch.IsLatched);
    Assert.Equal(1, handler.RequestCount);
  }

  // ReadErrorCodeAsync tries error/Error/code/Code (JSON property lookup is case-sensitive), and
  // HandledAsMarkReadDark's remarks promise `error`/`code`. All four must latch.
  // ⚠ The VALUE comparison is NOT case-insensitive — HandledAsMarkReadDark tests
  // `errorCode != "markread_disabled"` with ordinal equality, so "MarkRead_Disabled" would NOT
  // latch. That is correct (ADR-024 §3.3 defines one literal code), so there is deliberately no
  // case-variant of the value here.
  [Theory]
  [InlineData("error")]
  [InlineData("Error")]
  [InlineData("code")]
  [InlineData("Code")]
  public async Task Dark409_Latches_WhateverPropertyCarriesTheCode(string propertyName)
  {
    var body = "{\"" + propertyName + "\":\"markread_disabled\"}";
    var handler = new MockHttpHandler(body, HttpStatusCode.Conflict);
    var latch = new GvMarkReadDarkLatch();
    var svc = BuildSvc(handler, markReadEnabled: true, latch);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));

    Assert.True(latch.IsLatched);
    Assert.Equal(1, handler.RequestCount);
  }

  // GV-6: the only state where TryLatch()'s RETURN VALUE is live — two callers inside
  // HandledAsMarkReadDark at once, which is exactly the "concurrent Blazor circuits over one
  // singleton latch" case GvMarkReadDarkLatch's remarks describe. Dark409_LogsOneWarning_... above
  // cannot reach it: its calls 2-3 short-circuit at IsLatched and never enter the method, so it
  // would still pass if the TryLatch() guard around the LogWarning were deleted.
  //
  // The rendezvous reaching N=2 IS the proof this test is meaningful rather than incidental:
  // SendAsync does not answer until BOTH requests have arrived, so neither caller can have seen a
  // 409 — and therefore latched — before the other cleared its own IsLatched check. Without the
  // gate the two POSTs would serialise and this would be another IsLatched test in a costume.
  //
  // ⚠ The 30s wait inside Rendezvous is the ONE bounded wait in this test, and it is a deadlock
  // guard rather than a synchronisation device: a regression that stops one caller reaching the
  // handler turns an infinite hang into a failed assertion. It can never turn a pass into a
  // spurious failure — a saturated runner only makes the gate open later, and every assertion
  // below runs after both calls have already completed, against no clock at all. That is the
  // direction CLAUDE.md § Test Timing asks a timing dependency to fail in: this one can only fail.
  //
  // Two sinks, not one: the services log from different threads and CapturingLogger appends to a
  // bare List<T> with no lock, so a shared sink would be an unsynchronised race in the test itself.
  [Fact]
  public async Task Dark409_TwoCircuitsRacingTheFirstPost_StillLogOneWarningTotal()
  {
    var gate = new Rendezvous(participants: 2);
    var latch = new GvMarkReadDarkLatch();
    var entriesA = new List<(LogLevel Level, string Message)>();
    var entriesB = new List<(LogLevel Level, string Message)>();

    var a = BuildSvcOver(new GatedHandler(gate, DarkBody, HttpStatusCode.Conflict), latch,
      new CapturingLogger<GvBridgeApiService>(entriesA));
    var b = BuildSvcOver(new GatedHandler(gate, DarkBody, HttpStatusCode.Conflict), latch,
      new CapturingLogger<GvBridgeApiService>(entriesB));

    await Task.WhenAll(a.MarkVoicemailReadAsync("vm1"), b.MarkSmsThreadReadAsync("t1"));

    Assert.True(gate.AllArrived,
      "both callers must have been inside the handler together, or this test proves nothing");
    var dark = entriesA.Concat(entriesB)
      .Where(e => e.Message.Contains("GV mark-read is dark", StringComparison.Ordinal))
      .ToList();
    Assert.Single(dark);
    Assert.Equal(LogLevel.Warning, dark[0].Level);
    Assert.True(latch.IsLatched);
  }

  // Flag-on service over a caller-supplied handler. BuildSvc above takes MockHttpHandler
  // concretely (its callers assert on RequestCount); this one widens to HttpMessageHandler for
  // GatedHandler rather than changing the shared helper's signature.
  private static GvBridgeApiService BuildSvcOver(HttpMessageHandler handler,
    GvMarkReadDarkLatch latch, ILogger<GvBridgeApiService> logger)
  {
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:MarkReadEnabled"] = "true" })
      .Build();
    return new GvBridgeApiService(client, logger, config, latch);
  }

  /// <summary>
  /// Releases every participant only once ALL of them have arrived. No clock, no sleep and no
  /// polling: an Interlocked arrival count plus one TaskCompletionSource. The continuations are
  /// forced asynchronous so the last arrival does not run the others inline on its own stack,
  /// which would serialise the very race this exists to create.
  /// </summary>
  private sealed class Rendezvous(int participants)
  {
    // Deadlock guard only — see the racing test's comment. Never a synchronisation window.
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _all =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _arrived;

    /// <summary>True once every participant arrived; false means the guard fired instead.</summary>
    public bool AllArrived => _all.Task.IsCompletedSuccessfully;

    public Task ArriveAndWaitAsync()
    {
      if (Interlocked.Increment(ref _arrived) == participants)
      {
        _all.TrySetResult();
      }
      return _all.Task.WaitAsync(DeadlockGuard);
    }
  }

  /// <summary>
  /// Fixed-response handler that parks each request on a shared <see cref="Rendezvous"/> before
  /// answering, so N callers are guaranteed to be inside the service at the same moment. Private
  /// and nested on purpose: the assembly-shared MockHttpHandler gains nothing from a gate only
  /// this row needs.
  /// </summary>
  private sealed class GatedHandler(Rendezvous gate, string body, HttpStatusCode statusCode)
    : HttpMessageHandler
  {
    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      await gate.ArriveAndWaitAsync();
      return new HttpResponseMessage(statusCode)
      {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
      };
    }
  }
}
