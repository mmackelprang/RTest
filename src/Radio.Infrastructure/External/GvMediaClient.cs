using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.External;

/// <summary>
/// Fetches GV recordings server-side into a local file (ADR-029 D3 §5).
///
/// <para>
/// Shaped after PhoneContactLookupService — same folder, same host, typed HttpClient, options via
/// IOptionsMonitor — with two deliberate differences. It does NOT degrade silently to a fallback
/// value: a caller that asked for audio and got none needs to know why, so failures throw a
/// GvMediaUnavailableException carrying a reason. And its masking is stricter: the raw media id
/// reaches no log message, no log argument, and no exception message.
/// </para>
/// </summary>
public sealed class GvMediaClient
{
  /// <summary>
  /// Bytes per second assumed when bounding a download. GV voicemail is MP3 at roughly 64 kbps
  /// (~8 000 B/s); 32 000 leaves four times that headroom while still bounding the read, so a
  /// misbehaving or hostile upstream cannot exhaust memory on an N100 that already correlates
  /// CPU pressure with audible distortion.
  /// </summary>
  private const int AssumedMaxBytesPerSecond = 32_000;

  /// <summary>
  /// The route prefix every fetch must stay under. Used both to BUILD the path and to check the
  /// built Uri, so the two cannot drift apart into a check that passes whatever it is given.
  /// </summary>
  private const string VoicemailPathPrefix = "/api/gvbridge/voicemail/";

  private readonly ILogger<GvMediaClient> _logger;
  private readonly IOptionsMonitor<GvMediaOptions> _options;
  private readonly HttpClient _httpClient;
  private readonly GvMediaCache _cache;

  /// <summary>Creates the client over its typed HttpClient and the shared cache.</summary>
  public GvMediaClient(
    ILogger<GvMediaClient> logger,
    IOptionsMonitor<GvMediaOptions> options,
    HttpClient httpClient,
    GvMediaCache cache)
  {
    _logger = logger;
    _options = options;
    _httpClient = httpClient;
    _cache = cache;
  }

  /// <summary>
  /// Returns a local path for a voicemail recording, fetching on a cache miss.
  /// </summary>
  /// <param name="voicemailId">The provider's recording id.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <exception cref="GvMediaUnavailableException">
  /// The recording could not be produced. <see cref="GvMediaUnavailableException.Reason"/> says why.
  /// </exception>
  public async Task<string> GetVoicemailFileAsync(
    string voicemailId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(voicemailId);

    var options = _options.CurrentValue;
    var masked = GvMediaCache.MaskFor(voicemailId);

    if (!options.Enabled)
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Disabled, $"GvMedia is disabled; refusing to fetch {masked}.");
    }

    var cached = _cache.TryGetPath(voicemailId);
    if (cached is not null)
    {
      _logger.LogDebug("GV voicemail {MaskedId} served from cache", masked);
      return cached;
    }

    var uri = BuildVoicemailUri(options.BaseUrl, voicemailId, masked);
    var content = await FetchAsync(uri, options, masked, cancellationToken);

    var path = await _cache.WriteAsync(voicemailId, content, cancellationToken);
    _logger.LogInformation(
      "GV voicemail {MaskedId} fetched ({Bytes} bytes) and materialised", masked, content.Length);
    return path;
  }

  /// <summary>
  /// Builds the absolute fetch URI from the server's OWN configuration plus a media id.
  /// </summary>
  /// <remarks>
  /// ⚠ Deliberately NOT <c>new Uri(baseUri, mediaId)</c>, and deliberately not a relative request
  /// against HttpClient.BaseAddress. PR 1's review found that under RFC 3986 §4.2 a relative
  /// reference carrying a scheme is not relative at all — it resolves as ABSOLUTE — so
  /// "http:evil.example" would have escaped the configured host through exactly that call.
  /// EventPlaybackRequest.ValidateMediaId now allow-lists the id to [A-Za-z0-9._~-], which refuses
  /// ':' outright and closes that class. This method does not rely on that: it places the id in a
  /// path segment via UriBuilder, which cannot alter scheme or authority, and then COMPARES scheme,
  /// authority AND its own route prefix against what it built, rather than asserting that they
  /// cannot have changed. The prefix is part of that comparison because scheme and authority alone
  /// are not the whole of "the same place" — see the check itself for the dot-segment case.
  ///
  /// Uri.EscapeDataString is a no-op over the allow-listed set, and is applied anyway so this stays
  /// correct if the allow-list is ever loosened. It introduces no '%' for the allow-listed set, so
  /// UriBuilder.Path cannot double-escape.
  /// </remarks>
  internal static Uri BuildVoicemailUri(string baseUrl, string mediaId, string maskedId)
  {
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
        || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Transport, "GvMedia:BaseUrl is not an absolute http(s) URI.");
    }

    // UriBuilder is documented to signal an unassemblable URI with UriFormatException, from the
    // Path setter and from the Uri getter alike, and an escaping UriFormatException would put this
    // method outside the failure taxonomy in exactly the way a mid-body reset used to be: a caller
    // left holding an exception with no Reason on it.
    //
    // ⚠ Honest about what this guard is. No input reachable through this method was found to
    // trigger it on .NET 10 - Uri.EscapeDataString neutralised every candidate tried, including a
    // 200 000-character id and lone surrogates, which it folds to U+FFFD rather than rejecting. It
    // is kept because it makes the taxonomy hold by construction rather than by that measurement,
    // which is a property of the current runtime and not of this code. Do not read it as evidence
    // that a reachable case is known.
    Uri candidate;
    try
    {
      var builder = new UriBuilder(baseUri)
      {
        Path = $"{VoicemailPathPrefix}{Uri.EscapeDataString(mediaId)}/audio",
        Query = string.Empty,
        Fragment = string.Empty
      };
      candidate = builder.Uri;
    }
    catch (UriFormatException ex)
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Transport, $"Could not build a fetch URI for {maskedId}.", ex);
    }

    if (!string.Equals(candidate.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(candidate.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase))
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Transport,
        $"Refusing to fetch {maskedId} outside the configured GvMedia host.");
    }

    // ⚠ Scheme and authority are not the whole of "the same place". Uri compresses dot segments, so
    // an id of ".." turns /api/gvbridge/voicemail/../audio into /api/gvbridge/audio — same host,
    // different route — and Uri.EscapeDataString leaves ".." untouched because both characters are
    // unreserved. ValidateMediaId rejects "." and ".." already; this method deliberately does not
    // rely on that validator, which is the whole reason it compares rather than asserts, so it
    // checks its own prefix too.
    //
    // Honest about the residual: this catches an id that walks OUT of the prefix. An id of "."
    // collapses to /api/gvbridge/voicemail/audio, which is a different route but still under the
    // prefix, so only ValidateMediaId refuses that one.
    if (!candidate.AbsolutePath.StartsWith(VoicemailPathPrefix, StringComparison.Ordinal))
    {
      throw new GvMediaUnavailableException(
        GvMediaFailure.Transport,
        $"Refusing to fetch {maskedId} outside the configured GvMedia media route.");
    }

    return candidate;
  }

  private async Task<byte[]> FetchAsync(
    Uri uri, GvMediaOptions options, string masked, CancellationToken cancellationToken)
  {
    HttpResponseMessage response;
    try
    {
      response = await _httpClient.GetAsync(
        uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
      throw TimedOut(masked, ex);
    }
    catch (HttpRequestException ex)
    {
      _logger.LogWarning("GV voicemail {MaskedId} fetch failed below HTTP", masked);
      throw new GvMediaUnavailableException(
        GvMediaFailure.Transport, $"Transport failure fetching {masked}.", ex);
    }

    using (response)
    {
      if (!response.IsSuccessStatusCode)
      {
        var reason = (int)response.StatusCode switch
        {
          404 => GvMediaFailure.NotFound,
          401 or 403 => GvMediaFailure.Unauthorized,
          _ => GvMediaFailure.Upstream
        };

        // Warning rather than Debug: since LOG-11 the journal carries Warning and above, and a 502
        // here is the GV auth blackout, which is the thing an operator is most often diagnosing.
        _logger.LogWarning(
          "GV voicemail {MaskedId} fetch returned {StatusCode} ({Reason})",
          masked, (int)response.StatusCode, reason);

        throw new GvMediaUnavailableException(
          reason, $"Fetch of {masked} returned {(int)response.StatusCode}.");
      }

      var maxBytes = (long)Math.Max(1, options.MaxPlaybackSeconds) * AssumedMaxBytesPerSecond;

      if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
      {
        _logger.LogWarning(
          "GV voicemail {MaskedId} declared {Declared} bytes, over the {Max} byte bound",
          masked, declared, maxBytes);
        throw new GvMediaUnavailableException(
          GvMediaFailure.TooLarge, $"{masked} declared {declared} bytes, over the fetch bound.");
      }

      // ⚠ The body phase is inside a try of its own, and that is not tidiness. Under
      // ResponseHeadersRead the headers arrive cheaply and the body is where the time is spent, so
      // the COMMON timeout and the realistic gvbridge blackout — a connection reset mid-body — both
      // land here rather than on GetAsync above. Left uncaught they escaped as a bare
      // TaskCanceledException / HttpIOException carrying no Reason at all, which would have made
      // this class's own summary ("failures throw a GvMediaUnavailableException carrying a reason")
      // false for the likeliest failure, and left PR 3 with nothing to map.
      try
      {
        // Read with an explicit bound rather than ReadAsByteArrayAsync: Content-Length is advisory
        // and may be absent, so the bound has to hold while streaming too.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
          if (buffer.Length + read > maxBytes)
          {
            _logger.LogWarning(
              "GV voicemail {MaskedId} exceeded the {Max} byte bound while streaming", masked, maxBytes);
            throw new GvMediaUnavailableException(
              GvMediaFailure.TooLarge, $"{masked} exceeded the fetch bound while streaming.");
          }
          buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
          _logger.LogWarning("GV voicemail {MaskedId} fetch returned an empty body", masked);
          throw new GvMediaUnavailableException(
            GvMediaFailure.Upstream, $"Fetch of {masked} returned an empty body.");
        }

        return buffer.ToArray();
      }
      catch (GvMediaUnavailableException)
      {
        // TooLarge and Upstream are raised deliberately a few lines up and already carry the right
        // Reason. Rethrow first so the transport catch below cannot relabel them.
        throw;
      }
      catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
      {
        throw TimedOut(masked, ex);
      }
      catch (Exception ex) when (ex is HttpRequestException or IOException)
      {
        // HttpIOException derives from IOException, so a mid-body reset lands here. A timeout that
        // surfaces as an IOException rather than a cancellation is reported as Transport; both are
        // retryable, so the distinction costs the caller nothing.
        _logger.LogWarning(
          "GV voicemail {MaskedId} fetch failed below HTTP while reading the body", masked);
        throw new GvMediaUnavailableException(
          GvMediaFailure.Transport, $"Transport failure reading the body of {masked}.", ex);
      }
    }
  }

  /// <summary>
  /// Logs and builds the Timeout failure. Shared by the header phase and the body phase because
  /// HttpClient's own timeout can elapse in either, and both must carry the same Reason.
  /// </summary>
  /// <remarks>
  /// HttpClient surfaces its own timeout as a cancellation the caller did not request, which is what
  /// the callers' <c>when</c> filters test. That is not exact: a caller token cancelled BETWEEN the
  /// throw and the filter reads as a timeout here. The race is narrow and accepted.
  ///
  /// ⚠ The logged number is HttpClient.Timeout, NOT options.FetchTimeoutSeconds. They are not the
  /// same value: GvMediaServiceExtensions snapshots the option once at registration, while
  /// IOptionsMonitor tracks the live SQLite config bridge — so after a UI edit the option says one
  /// thing and the client still enforces another. Logging the option would hand the operator a
  /// timeout that did not govern the request they are diagnosing.
  /// </remarks>
  private GvMediaUnavailableException TimedOut(string masked, Exception inner)
  {
    _logger.LogWarning(
      "GV voicemail {MaskedId} fetch timed out after {Seconds}s",
      masked, _httpClient.Timeout.TotalSeconds);
    return new GvMediaUnavailableException(
      GvMediaFailure.Timeout, $"Timed out fetching {masked}.", inner);
  }
}
