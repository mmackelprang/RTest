using System.Net;
using Microsoft.Extensions.Logging;
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

  /// <summary>
  /// An HttpContent that declares NO length and hands out a stream which yields a fixed number of
  /// bytes and then either ends or fails. It exists because ByteArrayContent sets Content-Length,
  /// so it can only ever reach FetchAsync's declared-length branch — the streaming branch and every
  /// body-phase failure need a content type that behaves like a real chunked response.
  /// </summary>
  private sealed class ScriptedContent : HttpContent
  {
    private readonly long _bytesToYield;
    private readonly Func<Exception>? _failure;

    public ScriptedContent(long bytesToYield, Func<Exception>? failure)
    {
      _bytesToYield = bytesToYield;
      _failure = failure;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
      throw new NotSupportedException("This content is only ever read as a stream.");

    // false is the whole point: no Content-Length header, so the declared-length check is skipped
    // and the bound has to hold while streaming.
    protected override bool TryComputeLength(out long length)
    {
      length = 0;
      return false;
    }

    protected override Task<Stream> CreateContentReadStreamAsync() =>
      Task.FromResult<Stream>(new ScriptedStream(_bytesToYield, _failure));
  }

  /// <summary>
  /// Yields zero bytes then throws, or yields N bytes then ends. ⚠ The failure is thrown
  /// SYNCHRONOUSLY from the read — nothing here sleeps or waits for a real timeout to elapse
  /// (CLAUDE.md § "Test Timing"): a test that waited on HttpClient.Timeout would be racing a wall
  /// clock against a wall clock.
  /// </summary>
  private sealed class ScriptedStream : Stream
  {
    private readonly long _bytesToYield;
    private readonly Func<Exception>? _failure;
    private long _yielded;

    public ScriptedStream(long bytesToYield, Func<Exception>? failure)
    {
      _bytesToYield = bytesToYield;
      _failure = failure;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      var taken = Take(count);
      if (taken == 0)
      {
        return Finish();
      }
      Array.Clear(buffer, offset, taken);
      return taken;
    }

    public override ValueTask<int> ReadAsync(
      Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      var taken = Take(buffer.Length);
      if (taken == 0)
      {
        return ValueTask.FromResult(Finish());
      }
      buffer.Span[..taken].Clear();
      return ValueTask.FromResult(taken);
    }

    private int Take(int wanted)
    {
      var available = _bytesToYield - _yielded;
      if (available <= 0)
      {
        return 0;
      }
      var taken = (int)Math.Min(wanted, available);
      _yielded += taken;
      return taken;
    }

    private int Finish() => _failure is null ? 0 : throw _failure();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();
  }

  private (GvMediaClient Client, StubHandler Handler) CreateClient(
    Func<HttpRequestMessage, HttpResponseMessage> respond,
    int capMegabytes = 50,
    bool enabled = true,
    int maxPlaybackSeconds = 300)
  {
    var options = new GvMediaOptions
    {
      Enabled = enabled,
      BaseUrl = "http://radio:5004",
      CacheDirectory = _dir,
      CacheMaxMegabytes = capMegabytes,
      MaxPlaybackSeconds = maxPlaybackSeconds
    };
    var monitor = new StaticOptionsMonitor<GvMediaOptions>(options);
    var handler = new StubHandler(respond);
    var http = new HttpClient(handler);
    // The CAPTURING logger, not NullLogger. The masking pin below claims to cover "every path and
    // every level", and the cache logs too - a NullLogger here would quietly put those log sites
    // outside the pin whose comment says otherwise. The cache is clean today; this is what stops a
    // future edit to it from leaking an id without failing a test.
    var cache = new GvMediaCache(_logs.CreateLogger<GvMediaCache>(), monitor);
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
  // ⚠ NotFound was `true` here until PR 3. It changed deliberately, not because the test was
  // inconvenient: RotaryPhone's GvVoicemailController.GetAudio answers 404 when its voicemail LIST
  // call fails, which is the Google Voice auth blackout, so a 404 from this upstream is ambiguous
  // between "gone" and "try again in a few minutes". See GvMediaUnavailableException.IsPermanent.
  // The REASON is unchanged and still distinct; only the retryability claim moved.
  [InlineData(HttpStatusCode.NotFound, GvMediaFailure.NotFound, false)]
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
    // ByteArrayContent sets Content-Length, so this is the DECLARED-length branch and only that.
    var oversize = new byte[300 * 32_000 + 1];
    var (client, _) = CreateClient(_ => Audio(oversize));

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.TooLarge, ex.Reason);
  }

  [Fact]
  public async Task AnOversizeBodyWithNoContentLength_IsRefusedWhileStreaming()
  {
    // The streaming half of the bound, which the test above cannot reach. It matters because
    // nobody has yet established whether the real gvbridge sends Content-Length at all - if it does
    // not, this is the ONLY branch that ever runs, and it was untested.
    //
    // MaxPlaybackSeconds = 1 puts the bound at 32 000 bytes, so this streams kilobytes rather than
    // the ~9.6 MB the default bound would need.
    var content = new ScriptedContent(1_000_000, failure: null);
    Assert.Null(content.Headers.ContentLength); // the precondition the whole test rests on

    var (client, _) = CreateClient(
      _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content },
      maxPlaybackSeconds: 1);

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.TooLarge, ex.Reason);
  }

  // ── The body phase is inside the taxonomy too ─────────────────────────────
  // Under ResponseHeadersRead the headers are cheap and the body is where the time and the failures
  // are, so these two are the LIKELIEST failures in production — and before this they escaped as a
  // bare HttpIOException / TaskCanceledException carrying no Reason at all.

  [Fact]
  public async Task ABodyThatDiesMidStream_IsClassifiedAsTransport()
  {
    // The realistic gvbridge blackout failure: the connection is reset after the headers land.
    var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ScriptedContent(4096, () => new IOException("connection reset by peer"))
    });

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.Transport, ex.Reason);
    Assert.False(ex.IsPermanent);
  }

  [Fact]
  public async Task ABodyPhaseTimeout_IsClassifiedAsTimeout()
  {
    // HttpClient.Timeout elapsing during the body is the COMMON timeout here, and it surfaces as a
    // cancellation the caller never asked for. Thrown synchronously — see ScriptedStream.
    var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ScriptedContent(
        4096, () => new TaskCanceledException("HttpClient.Timeout elapsed."))
    });

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.Timeout, ex.Reason);
  }

  [Fact]
  public async Task ABodyPhaseFailure_DoesNotRelabelTheClientsOwnRefusals()
  {
    // The rethrow guard: an empty body is refused as Upstream from inside the same try that now
    // catches transport failures, so it must not come back out as Transport.
    var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ScriptedContent(0, failure: null)
    });

    var ex = await Assert.ThrowsAsync<GvMediaUnavailableException>(
      () => client.GetVoicemailFileAsync(RawId));

    Assert.Equal(GvMediaFailure.Upstream, ex.Reason);
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
  [InlineData(".")]
  [InlineData("..")]
  public void ASchemeOrPathBearingIdCannotMoveTheFetchOffTheConfiguredRoute(string hostileId)
  {
    // PR 1's review found the deny-list defeated by a scheme-bearing id: under RFC 3986 §4.2 a
    // relative reference carrying a scheme resolves as ABSOLUTE, so new Uri(base, id) escaped the
    // base. EventPlaybackRequest now allow-lists the id; this pins that GvMediaClient does not
    // reintroduce the hole even if it is handed an id that never went through that validator.
    //
    // Two acceptable outcomes, and the distinction is real rather than a hedge: most of these are
    // neutralised by escaping and come back as a URI still on the host and under the route, while
    // ".." is refused outright because Uri's dot-segment compression moves it off the route
    // entirely. Both mean "the fetch did not move"; only one of them can return a Uri.
    Uri uri;
    try
    {
      uri = GvMediaClient.BuildVoicemailUri("http://radio:5004", hostileId, "gvm:test");
    }
    catch (GvMediaUnavailableException ex)
    {
      Assert.Equal(GvMediaFailure.Transport, ex.Reason);
      return;
    }

    Assert.Equal("radio:5004", uri.Authority);
    Assert.Equal("http", uri.Scheme);
    Assert.StartsWith("/api/gvbridge/voicemail/", uri.AbsolutePath, StringComparison.Ordinal);
  }

  [Fact]
  public void ADotDotIdIsRefused_BecauseUriCompressionWalksItOffTheRoute()
  {
    // Named separately so the theory above cannot pass vacuously by throwing for everything, and
    // because this is the case the prefix check exists for: /api/gvbridge/voicemail/../audio
    // collapses to /api/gvbridge/audio - same host, a completely different route.
    // Uri.EscapeDataString does not touch "..", both characters being unreserved, so escaping alone
    // never had a chance at this one.
    var ex = Assert.Throws<GvMediaUnavailableException>(
      () => GvMediaClient.BuildVoicemailUri("http://radio:5004", "..", "gvm:test"));

    Assert.Equal(GvMediaFailure.Transport, ex.Reason);
  }

  [Theory]
  // Unknown = 0 is the default value, so it is what a GvMediaUnavailableException constructed
  // without a reason carries. It was the one member this theory omitted, which meant "true ONLY for
  // Disabled" was asserted over seven of the enum's eight members.
  [InlineData(GvMediaFailure.Unknown, false)]
  [InlineData(GvMediaFailure.Disabled, true)]
  [InlineData(GvMediaFailure.NotFound, false)]
  [InlineData(GvMediaFailure.Unauthorized, false)]
  [InlineData(GvMediaFailure.Upstream, false)]
  [InlineData(GvMediaFailure.Timeout, false)]
  [InlineData(GvMediaFailure.Transport, false)]
  [InlineData(GvMediaFailure.TooLarge, false)]
  public void IsPermanentIsTrueOnlyForDisabled(GvMediaFailure reason, bool expected)
  {
    // NotFound is false on purpose and this is the assertion that says so. RotaryPhone's
    // GvVoicemailController.GetAudio answers 404 when its voicemail LIST call fails, which is the
    // GV auth blackout - so a 404 here is ambiguous between "gone" and "try again", and this side
    // cannot tell. If RotaryPhone ever propagates Succeeded so the route answers 502 during a
    // blackout, THIS test is what should change, deliberately, alongside the doc that explains it.
    Assert.Equal(expected, new GvMediaUnavailableException(reason, "masked").IsPermanent);
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
