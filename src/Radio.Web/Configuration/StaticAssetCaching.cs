namespace Radio.Web.Configuration;

using Microsoft.AspNetCore.Builder;

/// <summary>
/// Cache-Control policy for everything <c>UseStaticFiles</c> serves.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseStaticFiles</c> sends <c>ETag</c> and <c>Last-Modified</c> but sets no
/// <c>Cache-Control</c> at all. A response carrying no explicit freshness information is
/// not thereby uncacheable — RFC 9111 §4.2.2 lets a cache invent a heuristic lifetime from
/// <c>Last-Modified</c>, so a browser may reuse a stored asset for some interval of its own
/// choosing <em>without asking the server whether it changed</em>. That is the whole defect:
/// the server offers a cheap way to check (the ETag) and never tells the client to use it.
/// </para>
/// <para>
/// On 2026-09-02 the kiosk was measured serving a <c>design-system.css</c> that predated a
/// deploy earlier the same day (775 rules, missing the entire ENC-4 block). The deploy's own
/// verification passed while that was true, and correctly so: <c>OPS-1</c> compares the git
/// SHA reported by each service's <c>/api/health/version</c>, which is stamped into the
/// <em>assembly</em>. It is an accurate statement about which binary is running and says
/// nothing about which bytes a browser is painting. A CSS-only change can therefore land,
/// verify green on both services, and still not be on the panel.
/// </para>
/// <para>
/// Restarting the browser does not resolve it either — Chrome's HTTP cache lives in the
/// profile directory and outlives the process, so the kiosk relaunch at the end of a deploy
/// re-reads the same stale entries. <c>Deploy-ToLinux.ps1</c> deletes that cache directory
/// for the kiosk profile, which is a mitigation on one profile on one box; it does nothing
/// for a laptop or tablet on the LAN pointed at <c>radio:5002</c>, and nothing at all under
/// <c>-NoRestart</c>. Setting the header fixes it for every client instead of one.
/// </para>
/// <para>
/// <c>no-cache</c> does <em>not</em> mean "do not store". It means "do not reuse without
/// revalidating": the browser still keeps the response, it just has to send a conditional
/// request first, which the existing <c>ETag</c> answers with a <c>304</c> and no body. The
/// clients here are the kiosk Chrome on this same box and occasional LAN browsers, so that
/// round-trip is not a cost worth trading the guarantee for.
/// </para>
/// <para>
/// The policy is applied uniformly, and that is a decision rather than an oversight: no URL
/// this app links is content-hashed, so there is no class of asset that can safely be cached
/// hard. This is Blazor <em>Server</em>, so <c>_framework/</c> carries <c>blazor.web.js</c> at
/// that fixed path rather than the fingerprinted asset set a WebAssembly app would have, and
/// <c>_content/Radzen.Blazor/*</c>, <c>Radio.Web.styles.css</c>, <c>css/*</c>, <c>js/*</c> and
/// <c>fonts/*</c> are all served under stable names too. A long <c>max-age</c> on any of them
/// would be this same bug with a longer fuse. Caching an asset hard requires fingerprinting it
/// first — the framework route for that is <c>MapStaticAssets</c> plus <c>@Assets[...]</c> at
/// every reference, which is a larger change than this one and is not what landed here.
/// </para>
/// </remarks>
internal static class StaticAssetCaching
{
  /// <summary>
  /// The <c>Cache-Control</c> value applied to every static asset.
  /// </summary>
  /// <remarks>
  /// Revalidate-always. See the type-level remarks for why this is not <c>no-store</c> (which
  /// would forfeit the 304s) and not a <c>max-age</c> (which would reintroduce the defect).
  /// </remarks>
  internal const string CacheControlValue = "no-cache";

  /// <summary>
  /// Builds the <see cref="StaticFileOptions"/> used by Radio.Web's static file middleware.
  /// </summary>
  /// <remarks>
  /// <c>OnPrepareResponse</c> runs for the <c>304</c> path as well as the <c>200</c> path, so a
  /// revalidated asset carries the header too and the browser's stored copy keeps its
  /// instruction to revalidate again next time.
  /// </remarks>
  internal static StaticFileOptions CreateOptions() => new()
  {
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = CacheControlValue
  };
}
