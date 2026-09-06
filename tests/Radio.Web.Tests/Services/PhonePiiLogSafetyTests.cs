using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radio.Core.Utilities;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Services;

/// <summary>
/// <c>PHN-5</c> <c>T3</c>: drives the four <c>Radio.Web</c> sites that logged a raw phone number
/// (<c>P8</c>–<c>P11</c>) and asserts none of them still does.
/// </summary>
/// <remarks>
/// ⚠ <b>These four are the half of the row that was NOT latent.</b> <c>Radio.Web</c> never binds
/// <c>PhoneIntegrationOptions</c>, all four types are registered unconditionally, and
/// <c>PhoneHubService.StartAsync()</c> is called from <c>Program.cs</c> inside no <c>if</c>. So
/// unlike the <c>Radio.API</c> sites, nothing switched these off. <c>P8</c> is the sharpest:
/// <c>Radio.Web</c>'s Console sink carries no <c>restrictedToMinimumLevel</c>, so its Information
/// lines reach <c>journalctl -u radio-web</c> under systemd — the rule in <c>CLAUDE.md</c> that
/// says otherwise is a statement about <c>Radio.API</c> alone.
///
/// ⚠⚠ <b><see cref="CapturingLogger{T}"/> records <c>exception?.ToString()</c> as well as the
/// formatted message, and that is load-bearing rather than thorough.</b> Three of these four sites
/// log an exception, and the request URLs embed the number — so a harness that ignored the
/// exception would report green while the number leaked through a stack trace.
/// <see cref="Harness_ObservesTheExceptionChannel"/> proves it with a deliberate throw carrying the
/// sentinel, and it runs first for that reason.
///
/// The duplicate of <c>Radio.Infrastructure.Tests</c>' <c>CapturingLoggerProvider</c> is
/// deliberate: this is a separate assembly, and thirty lines here is the right price against
/// minting a shared test package for one type.
/// </remarks>
public class PhonePiiLogSafetyTests
{
  private const string Sentinel = "5550137424";
  private const string Last4 = "7424";

  private static void AssertMaskedNotLeaked(CapturingSink sink)
  {
    var messages = sink.Messages;

    // ⚠ Guard first, or everything below is satisfied by an empty list.
    Assert.NotEmpty(messages);

    foreach (var message in messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      // Catches a reinstated "***{last4}" mask, which the whole-number check sails past.
      Assert.DoesNotContain(Last4, message, StringComparison.Ordinal);
    }

    // Masked, not deleted — deletion would satisfy the sweep above just as well.
    Assert.Contains(messages,
      m => m.Contains(LogSafeText.ForPhone(Sentinel), StringComparison.Ordinal));
  }

  [Fact]
  public void Harness_ObservesTheExceptionChannel()
  {
    // Pins the HARNESS, not a component. If this fails, the three exception-arm tests below prove
    // nothing about exceptions and their green is meaningless.
    var sink = new CapturingSink();
    ILogger<PhonePiiLogSafetyTests> logger = sink.CreateLogger<PhonePiiLogSafetyTests>();

    logger.LogError(new InvalidOperationException($"boom {Sentinel}"), "a message with no PII");

    Assert.Contains(sink.Messages, m => m.Contains(Sentinel, StringComparison.Ordinal));
  }

  [Fact]
  public async Task P9_GvTrunkDialFailure_LogsNoRawNumber()
  {
    // The Error-level site. `ex` is kept (C-103): a failed dial's stack trace is the whole
    // diagnostic value of the line, so this proves the number is absent FROM it rather than
    // deleting it to be safe.
    var sink = new CapturingSink();
    var service = new GvTrunkApiService(
      ThrowingClient("gv trunk unreachable"), sink.CreateLogger<GvTrunkApiService>());

    var ok = await service.DialAsync(Sentinel);

    Assert.False(ok);
    AssertMaskedNotLeaked(sink);
    Assert.Contains(sink.Messages, m => m.Contains("gv trunk unreachable", StringComparison.Ordinal));
  }

  [Fact]
  public async Task P10_PbapLookupFailure_LogsNoRawNumber()
  {
    var sink = new CapturingSink();
    var service = new PbapApiService(
      ThrowingClient("pbap unreachable"), sink.CreateLogger<PbapApiService>());

    var (outcome, name) = await service.LookupNumberAsync(Sentinel);

    // The documented contract on a transient failure: Unavailable, so the caller does not cache it.
    Assert.Equal(ContactLookupOutcome.Unavailable, outcome);
    Assert.Null(name);
    AssertMaskedNotLeaked(sink);
    Assert.Contains(sink.Messages, m => m.Contains("pbap unreachable", StringComparison.Ordinal));
  }

  [Fact]
  public async Task P11_ContactResolutionFailure_LogsNoRawNumber()
  {
    // ⚠⚠ REACHING :173 IS HARDER THAN IT LOOKS, AND THE FIRST VERSION OF THIS TEST DID NOT.
    // ContactResolutionService's catch is explicitly a guard against the unexpected — its own
    // comment says "PbapApiService maps its own failures to Unavailable, but guard anyway" — and
    // that is exactly right: LookupNumberAsync wraps its request in catch(Exception) and returns
    // Unavailable, so NOTHING thrown inside it escapes. An earlier draft passed a null HttpClient
    // and asserted on the result; it went green, but the NRE was raised INSIDE PbapApiService's
    // try and swallowed there, so the test was exercising :104 and never reached :173 at all. It
    // was caught by the mutation run: restoring the raw argument at :173 left it PASSING.
    //
    // The only path out of LookupNumberAsync is a throw from its catch BLOCK, i.e. from the
    // _logger.LogDebug call in it — so the seam is a logger that throws. Contrived on purpose:
    // this is a defensive catch, and a defensive catch has no natural trigger.
    var sink = new CapturingSink();
    var pbap = new PbapApiService(ThrowingClient("pbap unreachable"), new ThrowingLogger<PbapApiService>());
    var service = new ContactResolutionService(pbap, sink.CreateLogger<ContactResolutionService>());

    var name = await service.ResolveAsync(Sentinel);

    Assert.Null(name);
    AssertMaskedNotLeaked(sink);
    // Prove we landed on :173 specifically, and not on some other line that happens to be safe.
    Assert.Contains(sink.Messages,
      m => m.Contains("Contact resolution failed", StringComparison.Ordinal));
  }

  [Fact]
  public void P8_IncomingCall_LogsNoRawNumberButStillRaisesItToSubscribers()
  {
    // ⭐ Driven through the internal seam Task 6 created, which the live
    // `.On<string, string>("IncomingCall", RaiseIncomingCallForTest)` registration points at — so
    // this exercises production rather than a copy of it. Same arrangement the file already used
    // for ReadStateChanged.
    var sink = new CapturingSink();
    var service = new PhoneHubService(
      sink.CreateLogger<PhoneHubService>(), new ConfigurationBuilder().Build());

    string? raisedId = null;
    string? raisedNumber = null;
    service.IncomingCall += (id, number) => { raisedId = id; raisedNumber = number; };

    service.RaiseIncomingCallForTest("phone-1", Sentinel);

    AssertMaskedNotLeaked(sink);

    // ⚠ Deliberately asserted: the RAW number still reaches subscribers, because the UI displays
    // it. This test exists so a future over-eager "mask everything" edit fails loudly.
    Assert.Equal("phone-1", raisedId);
    Assert.Equal(Sentinel, raisedNumber);
  }

  /// <summary>An HttpClient whose every request throws, so a failure arm is reached offline.</summary>
  private static HttpClient ThrowingClient(string reason) =>
    new(new ThrowingHandler(reason)) { BaseAddress = new Uri("http://unused.invalid") };

  private sealed class ThrowingHandler(string reason) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) =>
      throw new HttpRequestException(reason);
  }

  /// <summary>
  /// Captures every formatted log message at every level, plus the text of any exception.
  /// </summary>
  /// <remarks>
  /// <c>IsEnabled</c> returns <c>true</c> at every level, which is the only reason the Debug sites
  /// <c>P10</c> and <c>P11</c> are observable at all — nothing in either service's shipped
  /// configuration emits them today, and that is a volume setting rather than a safety one.
  /// Synchronized because <c>ContactResolutionService</c> logs from a continuation rather than the
  /// test thread; <see cref="Messages"/> hands back a snapshot so callers can enumerate freely.
  /// </remarks>
  private sealed class CapturingSink
  {
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
      get { lock (_messages) { return _messages.ToArray(); } }
    }

    public ILogger<T> CreateLogger<T>() => new CapturingLogger<T>(_messages);
  }

  /// <summary>
  /// A logger whose <c>Log</c> throws. The only way to make <c>PbapApiService.LookupNumberAsync</c>
  /// throw out of its own <c>catch(Exception)</c>, which is what
  /// <see cref="ContactResolutionService"/>'s defensive catch needs in order to run at all.
  /// </summary>
  private sealed class ThrowingLogger<T> : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) =>
      throw new InvalidOperationException("log sink exploded");
  }

  private sealed class CapturingLogger<T>(List<string> sink) : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      lock (sink)
      {
        sink.Add(formatter(state, exception));
        if (exception is not null)
        {
          // ⚠⚠ Not optional. Three of PHN-5's four Radio.Web sites log an exception and the
          // request URLs embed the number, so a harness recording only the formatted message would
          // report green while the number leaked through a stack trace.
          sink.Add(exception.ToString());
        }
      }
    }
  }
}
