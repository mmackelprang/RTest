using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Configuration;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

public sealed class GvMediaClientTests : IDisposable
{
  private const string RawId = "vm-secret-identifier-9876";

  private readonly string _dir = Path.Combine(
    Path.GetTempPath(), "gvmedia-client-tests-" + Guid.NewGuid().ToString("n"));

  private readonly CapturingLoggerProvider _logs = new();

  public void Dispose()
  {
    if (Directory.Exists(_dir))
    {
      Directory.Delete(_dir, recursive: true);
    }
  }

  private sealed class StubHandler : HttpMessageHandler
  {
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public int Calls { get; private set; }

    public Uri? LastUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Calls++;
      LastUri = request.RequestUri;
      return Task.FromResult(_respond(request));
    }
  }

  private (GvMediaClient Client, StubHandler Handler) CreateClient(
    Func<HttpRequestMessage, HttpResponseMessage> respond,
    int capMegabytes = 50,
    bool enabled = true)
  {
    var options = new GvMediaOptions
    {
      Enabled = enabled,
      BaseUrl = "http://radio:5004",
      CacheDirectory = _dir,
      CacheMaxMegabytes = capMegabytes
    };
    var monitor = new StaticOptionsMonitor<GvMediaOptions>(options);
    var handler = new StubHandler(respond);
    var http = new HttpClient(handler);
    var cache = new GvMediaCache(NullLogger<GvMediaCache>.Instance, monitor);
    var client = new GvMediaClient(
      _logs.CreateLogger<GvMediaClient>(), monitor, http, cache);
    return (client, handler);
  }

  private static HttpResponseMessage Audio(byte[] bytes) =>
    new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

  [Fact]
  public async Task Fetch_WritesTheRecordingAndReturnsItsPath()
  {
    var (client, _) = CreateClient(_ => Audio(new byte[2048]));

    var path = await client.GetVoicemailFileAsync(RawId);

    Assert.True(File.Exists(path));
    Assert.Equal(2048, new FileInfo(path).Length);
  }

  [Fact]
  public async Task ASecondCall_IsServedFromCacheAndNeverTouchesTheNetwork()
  {
    // The blackout property (ADR-029 §5.3): GV auth is dead ~9 minutes in every 20, so a replay
    // that went back to the network would 502 roughly 45% of the time.
    var (client, handler) = CreateClient(_ => Audio(new byte[1024]));

    await client.GetVoicemailFileAsync(RawId);
    await client.GetVoicemailFileAsync(RawId);

    Assert.Equal(1, handler.Calls);
  }

  [Fact]
  public async Task ASecondCall_RefetchesWhenTheCacheIsDisabled()
  {
    var (client, handler) = CreateClient(_ => Audio(new byte[1024]), capMegabytes: 0);

    await client.GetVoicemailFileAsync(RawId);
    await client.GetVoicemailFileAsync(RawId);

    Assert.Equal(2, handler.Calls);
  }

  [Fact]
  public async Task Fetch_IsRefusedWhenGvMediaIsDisabled_WithoutAnyRequest()
  {
    var (client, handler) = CreateClient(_ => Audio(new byte[16]), enabled: false);

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.Disabled, ex.Reason);
    Assert.True(ex.IsPermanent);
    Assert.Equal(0, handler.Calls);
  }

  [Theory]
  [InlineData(HttpStatusCode.NotFound, GvMediaFailure.NotFound, true)]
  [InlineData(HttpStatusCode.Unauthorized, GvMediaFailure.Unauthorized, false)]
  [InlineData(HttpStatusCode.Forbidden, GvMediaFailure.Unauthorized, false)]
  [InlineData(HttpStatusCode.BadGateway, GvMediaFailure.Upstream, false)]
  [InlineData(HttpStatusCode.ServiceUnavailable, GvMediaFailure.Upstream, false)]
  public async Task StatusCodesMapToDistinctReasons(
    HttpStatusCode status, GvMediaFailure expected, bool permanent)
  {
    // GV-6 and GV-8 are both open rows for collapsing this distinction. A 404 and a 502 need
    // opposite responses from the UI.
    var (client, _) = CreateClient(_ => new HttpResponseMessage(status));

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(expected, ex.Reason);
    Assert.Equal(permanent, ex.IsPermanent);
  }

  [Fact]
  public async Task AnOversizeBodyIsRefusedRatherThanBuffered()
  {
    var oversize = new byte[300 * 32_000 + 1];
    var (client, _) = CreateClient(_ => Audio(oversize));

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.TooLarge, ex.Reason);
  }

  // ── The masking pin ───────────────────────────────────────────────────────
  // ADR-029 §5.1 asks GvMediaClient to follow PhoneContactLookupService's masking discipline.
  // That file masks on ONE line and logs the raw number on four others, one of them at Warning.
  // The rule here is stronger and this test is what enforces it, on every path and every level —
  // and on exception messages too, because callers log exceptions.
  [Theory]
  [InlineData(HttpStatusCode.OK)]
  [InlineData(HttpStatusCode.NotFound)]
  [InlineData(HttpStatusCode.BadGateway)]
  public async Task TheRawMediaIdNeverReachesALogLineOrAnExceptionMessage(HttpStatusCode status)
  {
    var (client, _) = CreateClient(_ => status == HttpStatusCode.OK
      ? Audio(new byte[512])
      : new HttpResponseMessage(status));

    try
    {
      await client.GetVoicemailFileAsync(RawId);
      // Second call exercises the cache-hit path, which has its own log line.
      await client.GetVoicemailFileAsync(RawId);
    }
    catch (GvMediaUnavailableException ex)
    {
      Assert.DoesNotContain(RawId, ex.Message, StringComparison.Ordinal);
    }

    Assert.NotEmpty(_logs.Messages);
    Assert.All(_logs.Messages, m => Assert.DoesNotContain(RawId, m, StringComparison.Ordinal));
  }

  [Fact]
  public void TheMaskIsAHashPrefix_NotASuffixOfTheId()
  {
    // ***1234 is right for a phone number, because a human recognises one by its last four digits.
    // Nobody recognises a voicemail id, so a suffix would leak four characters for no benefit.
    var mask = GvMediaCache.MaskFor(RawId);

    Assert.StartsWith("gvm:", mask, StringComparison.Ordinal);
    Assert.DoesNotContain(RawId[^4..], mask, StringComparison.Ordinal);
  }

  // ── The SSRF pins ─────────────────────────────────────────────────────────
  [Fact]
  public async Task TheFetchedUriAlwaysStaysOnTheConfiguredHost()
  {
    var (client, handler) = CreateClient(_ => Audio(new byte[64]));

    await client.GetVoicemailFileAsync(RawId);

    Assert.NotNull(handler.LastUri);
    Assert.Equal("radio:5004", handler.LastUri!.Authority);
    Assert.Equal("http", handler.LastUri.Scheme);
    Assert.Equal($"/api/gvbridge/voicemail/{RawId}/audio", handler.LastUri.AbsolutePath);
  }

  [Theory]
  [InlineData("http:evil.example")]
  [InlineData("https://evil.example/payload.mp3")]
  [InlineData("//evil.example/payload.mp3")]
  [InlineData("../../etc/passwd")]
  public void ASchemeOrPathBearingIdCannotMoveTheFetchOffTheConfiguredHost(string hostileId)
  {
    // PR 1's review found the deny-list defeated by a scheme-bearing id: under RFC 3986 §4.2 a
    // relative reference carrying a scheme resolves as ABSOLUTE, so new Uri(base, id) escaped the
    // base. EventPlaybackRequest now allow-lists the id; this pins that GvMediaClient does not
    // reintroduce the hole even if it is handed an id that never went through that validator.
    var uri = GvMediaClient.BuildVoicemailUri("http://radio:5004", hostileId, "gvm:test");

    Assert.Equal("radio:5004", uri.Authority);
    Assert.Equal("http", uri.Scheme);
    Assert.StartsWith("/api/gvbridge/voicemail/", uri.AbsolutePath, StringComparison.Ordinal);
  }
}

/// <summary>Captures every formatted log message, at every level, for the masking pin.</summary>
internal sealed class CapturingLoggerProvider
{
  public List<string> Messages { get; } = [];

  public ILogger<T> CreateLogger<T>() => new CapturingLogger<T>(Messages);

  private sealed class CapturingLogger<T>(List<string> sink) : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      sink.Add(formatter(state, exception));
      if (exception is not null)
      {
        sink.Add(exception.ToString());
      }
    }
  }
}
