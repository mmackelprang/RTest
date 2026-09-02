namespace Radio.Web.Tests.Configuration;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Radio.Web.Configuration;

/// <summary>
/// Pins the static-asset Cache-Control policy. Regression guard for OPS-5: Radio.Web served
/// every static asset with an <c>ETag</c> and no <c>Cache-Control</c>, so browsers applied
/// heuristic freshness and reused stale copies without revalidating. The kiosk was measured
/// on 2026-09-02 painting a <c>design-system.css</c> that predated a deploy earlier that day,
/// while the deploy's own SHA verification passed — that check reads the git SHA stamped into
/// each assembly and is silent about static content.
///
/// The property these tests defend is that the middleware Radio.Web installs always stamps a
/// revalidate-always header, for every asset, on both the 200 and the 304 path.
/// </summary>
public class StaticAssetCachingTests
{
  private static StaticFileResponseContext ContextFor(HttpContext http) =>
    new(http, new NotFoundFileInfo("design-system.css"));

  [Fact]
  public void CreateOptions_InstallsAnOnPrepareResponseCallback()
  {
    // Guards the failure mode that caused the bug: no callback at all.
    var options = StaticAssetCaching.CreateOptions();

    Assert.NotNull(options.OnPrepareResponse);
  }

  [Fact]
  public void OnPrepareResponse_SetsRevalidateAlwaysCacheControl()
  {
    var options = StaticAssetCaching.CreateOptions();
    var http = new DefaultHttpContext();

    options.OnPrepareResponse(ContextFor(http));

    Assert.Equal("no-cache", http.Response.Headers.CacheControl);
  }

  [Fact]
  public void OnPrepareResponse_DoesNotEmitNoStore()
  {
    // no-store would forfeit the 304s that make revalidation cheap. The policy is
    // "revalidate before reuse", not "never keep a copy".
    var options = StaticAssetCaching.CreateOptions();
    var http = new DefaultHttpContext();

    options.OnPrepareResponse(ContextFor(http));

    Assert.DoesNotContain("no-store", http.Response.Headers.CacheControl.ToString());
  }

  [Fact]
  public void OnPrepareResponse_DoesNotEmitMaxAge()
  {
    // A max-age without content-hashed filenames is the same defect with a longer fuse.
    // Nothing Radio.Web links is fingerprinted, so no positive lifetime is safe here.
    var options = StaticAssetCaching.CreateOptions();
    var http = new DefaultHttpContext();

    options.OnPrepareResponse(ContextFor(http));

    Assert.DoesNotContain("max-age", http.Response.Headers.CacheControl.ToString());
  }

  [Theory]
  [InlineData(StatusCodes.Status200OK)]
  [InlineData(StatusCodes.Status304NotModified)]
  public void OnPrepareResponse_SetsHeaderRegardlessOfStatusCode(int statusCode)
  {
    // Pins that the callback itself is status-code agnostic: it stamps the header without
    // consulting Response.StatusCode, so whenever the middleware runs it on the revalidation
    // path the 304 carries the header too. That the middleware DOES run it there is a
    // framework behaviour this test cannot observe; it is verified against the appliance
    // (OPS-5 verification step 2) rather than asserted here.
    var options = StaticAssetCaching.CreateOptions();
    var http = new DefaultHttpContext();
    http.Response.StatusCode = statusCode;

    options.OnPrepareResponse(ContextFor(http));

    Assert.Equal("no-cache", http.Response.Headers.CacheControl);
  }

  [Fact]
  public void OnPrepareResponse_OverwritesAnyPreexistingCacheControl()
  {
    var options = StaticAssetCaching.CreateOptions();
    var http = new DefaultHttpContext();
    http.Response.Headers.CacheControl = "max-age=31536000";

    options.OnPrepareResponse(ContextFor(http));

    Assert.Equal("no-cache", http.Response.Headers.CacheControl);
  }
}
