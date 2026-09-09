namespace Radio.Web.Tests.Configuration;

using System.Net;
using Radio.Web.Tests.TestHelpers;

/// <summary>
/// Drives a real static-file request through the real Radio.Web pipeline.
/// </summary>
/// <remarks>
/// <see cref="StaticAssetCachingTests"/> pins the policy object; this pins that Program.cs actually
/// installs it. That is the gap worth closing here: the whole of OPS-5 rests on one argument at one
/// call site, and a rebase that dropped it — restoring the bare <c>app.UseStaticFiles()</c> — would
/// reintroduce the exact stale-asset bug with every policy unit test still green.
/// </remarks>
public class StaticAssetPipelineTests : IClassFixture<RadioWebFactory>
{
  private readonly RadioWebFactory _factory;

  public StaticAssetPipelineTests(RadioWebFactory factory) => _factory = factory;

  [Theory]
  [InlineData("/css/design-system.css")]
  [InlineData("/js/idle-dimmer.js")]
  [InlineData("/fonts/DSEG14Classic-Regular.woff2")]
  public async Task StaticAssets_AreServedWithRevalidateAlwaysCacheControl(string path)
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync(path);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
  }

  /// <summary>
  /// The header is what makes revalidation happen; the <c>ETag</c> is what makes it cheap. A policy
  /// that forced revalidation without an entity tag would turn every asset into a full re-download,
  /// so this pins that both halves are present on the same response.
  /// </summary>
  [Fact]
  public async Task StaticAssets_StillCarryAnETagToRevalidateAgainst()
  {
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/css/design-system.css");

    Assert.NotNull(response.Headers.ETag);
  }

  /// <summary>
  /// UX-1. A custom property that is CONSUMED but never DECLARED makes the whole declaration
  /// invalid at computed-value time, so the value is silently dropped — here that would leave the
  /// shimmer a flat block, which looks exactly like the bug UX-1 fixed, with a green build and a
  /// green suite. This test exists so <c>--skeleton-shimmer-highlight</c> cannot become one.
  /// </summary>
  /// <remarks>
  /// The hazard is not hypothetical in this stylesheet: <c>--signal-red-glow</c> and
  /// <c>--signal-green-glow</c> were consumed by three live <c>box-shadow</c>s and declared
  /// nowhere, so all three resolved to nothing. UI-8 (2026-09-09) resolved that by DELETING the
  /// references on an owner decision of NO GLOW rather than declaring the tokens, so this file no
  /// longer contains a surviving example — the hazard, and the reason for this test, are unchanged.
  /// </remarks>
  [Fact]
  public async Task SkeletonShimmerToken_IsBothDeclaredAndConsumed()
  {
    var css = await _factory.CreateClient().GetStringAsync("/css/design-system.css");

    Assert.Contains("--skeleton-shimmer-highlight:", css);
    Assert.Contains("var(--skeleton-shimmer-highlight)", css);

    // No fallback: var(--skeleton-shimmer-highlight, #38383F) would paper over a missing or
    // misspelled declaration and make the assertion above unfalsifiable.
    Assert.DoesNotContain("var(--skeleton-shimmer-highlight,", css);

    Assert.Contains("#38383F", DeclaredValueOf(css, "--skeleton-shimmer-highlight"));
  }

  /// <summary>
  /// UX-1. Pins the shimmer primitive: the highlight stop comes from the shimmer's own token, and
  /// the geometry is the three-stop one the owner actually judged.
  /// </summary>
  /// <remarks>
  /// ⚠ This asserts the CSS SOURCE TEXT. Neither this factory nor bUnit rasterises anything, so no
  /// test in this repository can show that the shimmer is visible — the evidence for that is the
  /// owner's two sittings at the panel, recorded under docs/uat/2026-09-08-ux1-shimmer-variants/.
  /// What it does do is fail on the pre-UX-1 stylesheet (whose middle stop was
  /// <c>var(--surface-overlay)</c>), which makes it a real regression gate rather than a tautology.
  /// The <c>background-size</c> assertion is load-bearing for a reason unrelated to colour: at 200%
  /// the tile stays wider than the element, so exactly one band is on screen and one pass completes
  /// every 750 ms. A smaller value would put two bands on a wide shape.
  /// </remarks>
  [Fact]
  public async Task SkeletonShimmer_TakesItsHighlightFromItsOwnTokenAtTheJudgedGeometry()
  {
    var css = await _factory.CreateClient().GetStringAsync("/css/design-system.css");
    var rule = SkeletonLoadingPrimitive(css);

    Assert.Contains("var(--surface-raised) 0%", rule);
    Assert.Contains("var(--skeleton-shimmer-highlight) 50%", rule);
    Assert.Contains("var(--surface-raised) 100%", rule);
    Assert.Contains("background-size: 200% 100%", rule);
    Assert.Contains("animation: shimmer 1.5s infinite", rule);
  }

  /// <summary>
  /// UX-1, the control. The whole reason the shimmer got its own token is that
  /// <c>--surface-overlay</c> is a SURFACE with a large consumer set — raising it to a visible
  /// shimmer amplitude would have re-themed every one of them. This asserts the two halves of "we
  /// did not do that": the shared token still declares its own value, and the shimmer no longer
  /// reads it.
  /// </summary>
  /// <remarks>
  /// A test that only checked the new token would pass just as happily on a diff that ALSO moved
  /// <c>--surface-overlay</c>, which is the failure this row exists to avoid.
  /// </remarks>
  [Fact]
  public async Task SkeletonShimmer_LeavesTheSharedSurfaceOverlayTokenWhereItWas()
  {
    var css = await _factory.CreateClient().GetStringAsync("/css/design-system.css");

    Assert.Contains("#1A1A1D", DeclaredValueOf(css, "--surface-overlay"));
    Assert.DoesNotContain("var(--surface-overlay)", SkeletonLoadingPrimitive(css));
  }

  /// <summary>
  /// UX-1. The reduced-motion override replaces <c>background</c> wholesale with a flat fill, so
  /// the gradient is never painted in that state and widening the shimmer cannot reach it. Pinned
  /// because the tempting "keep it in sync" edit — adding the shimmer token here — would
  /// re-introduce a gradient into the one state deliberately built without one.
  /// </summary>
  /// <remarks>
  /// ⚠ Unlike the three tests above, this one PASSES on the pre-UX-1 stylesheet, and that is not a
  /// defect — it pins a pre-existing invariant that this change must not disturb. It is a guard
  /// against a future edit, not a regression gate for this one. Measured, not assumed: reverting
  /// design-system.css to main and re-running this class gives 3 failed / 5 passed, and this is one
  /// of the five. The commit that introduced these tests said "four tests, all of which fail on the
  /// pre-UX-1 stylesheet"; that was wrong about this one.
  /// </remarks>
  [Fact]
  public async Task ReducedMotion_StillReplacesTheShimmerWithAFlatFill()
  {
    var css = await _factory.CreateClient().GetStringAsync("/css/design-system.css");

    var media = css.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal);
    Assert.True(media >= 0, "the reduced-motion block is gone or was renamed");

    var overrideStart = css.IndexOf(".skeleton-loading {", media, StringComparison.Ordinal);
    Assert.True(overrideStart >= 0, "the reduced-motion .skeleton-loading override is gone");
    var overrideRule = css[overrideStart..css.IndexOf('}', overrideStart)];

    Assert.Contains("animation: none", overrideRule);
    Assert.Contains("background: rgba(255, 255, 255, 0.05)", overrideRule);
    Assert.DoesNotContain("--skeleton-shimmer-highlight", overrideRule);
  }

  /// <summary>
  /// Returns the body of the top-level <c>.skeleton-loading</c> rule — the shimmer primitive. It is
  /// identified by its gradient rather than by position, so it cannot be confused with the
  /// reduced-motion override of the same selector further down the file.
  /// </summary>
  private static string SkeletonLoadingPrimitive(string css)
  {
    var start = css.IndexOf(".skeleton-loading {", StringComparison.Ordinal);
    Assert.True(start >= 0, "the .skeleton-loading primitive is gone or was renamed");

    var rule = css[start..css.IndexOf('}', start)];
    Assert.Contains("linear-gradient(90deg", rule);
    return rule;
  }

  /// <summary>
  /// Returns the declared value of a custom property, from the colon to the semicolon, so an
  /// assertion on the value does not also assert the column the file happens to pad it to.
  /// </summary>
  private static string DeclaredValueOf(string css, string property)
  {
    var start = css.IndexOf(property + ":", StringComparison.Ordinal);
    Assert.True(start >= 0, $"{property} is not declared");

    return css[start..css.IndexOf(';', start)];
  }
}
