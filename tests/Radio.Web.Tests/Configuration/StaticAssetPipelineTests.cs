namespace Radio.Web.Tests.Configuration;

using System.Net;
using System.Text.RegularExpressions;
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
  /// references on an owner decision of NO GLOW rather than declaring the tokens, so no LIVE example
  /// survives — though the text does: design-system.css still discusses
  /// <c>0 0 6px var(--signal-red-glow)</c> inside a comment. The hazard, and the reason for this
  /// test, are unchanged.
  /// <para>
  /// ⚠ That surviving comment is exactly why the declaration is matched with an anchored regex
  /// rather than <c>Contains</c>. A plain substring check is satisfied by a COMMENT: delete the
  /// declaration, leave <c>/* --skeleton-shimmer-highlight: #24242B removed by &lt;row&gt;; */</c>
  /// behind, and a <c>Contains</c>-based version of this test stays green while the shimmer renders
  /// flat — the precise bug the summary above says it prevents. This repo demonstrably writes that
  /// kind of comment, so the regex requires the declaration to be alone on its own line.
  /// </para>
  /// </remarks>
  [Fact]
  public async Task SkeletonShimmerToken_IsBothDeclaredAndConsumed()
  {
    var css = await _factory.CreateClient().GetStringAsync("/css/design-system.css");

    // Anchored to a whole line, so a mention inside a /* comment */ cannot satisfy it.
    Assert.Matches(new Regex(@"^\s*--skeleton-shimmer-highlight:\s*#24242B;\s*$", RegexOptions.Multiline), css);

    Assert.Contains("var(--skeleton-shimmer-highlight)", SkeletonLoadingPrimitive(css));

    // No fallback: var(--skeleton-shimmer-highlight, #24242B) would paper over a missing or
    // misspelled declaration and make the assertion above unfalsifiable. Scoped to the RULE, not
    // the file: a comment elsewhere warning "never write var(--skeleton-shimmer-highlight, …)"
    // would otherwise turn this red against a perfectly correct stylesheet.
    Assert.DoesNotContain("var(--skeleton-shimmer-highlight,", SkeletonLoadingPrimitive(css));
  }

  /// <summary>
  /// UX-1. Pins the shimmer primitive: the highlight stop comes from the shimmer's own token, and
  /// the geometry is the three-stop one the owner actually judged.
  /// </summary>
  /// <remarks>
  /// ⚠ This asserts the CSS SOURCE TEXT. Neither this factory nor bUnit rasterises anything, so no
  /// test in this repository can show that the shimmer is visible — the only evidence for that is
  /// the owner's eye at the panel. There have now been THREE sittings, and they do not agree: two
  /// under docs/uat/2026-09-08-ux1-shimmer-variants/ (dark room, then daylight) chose 56/#38383F,
  /// and a third on 2026-09-09 in afternoon light chose 36/#24242B, which is what this now pins. A
  /// dark-room re-check is outstanding and the change is gated on it; see docs/queue/UX-1.md. ⛔ A
  /// green run of this class is therefore not evidence that the shipped value is the right one — it
  /// only proves the stylesheet says what the last sitting said.
  /// What it does do is fail on the pre-UX-1 stylesheet (whose middle stop was
  /// <c>var(--surface-overlay)</c>), which makes it a real regression gate rather than a tautology.
  /// The <c>background-size</c> assertion is load-bearing for a reason unrelated to colour. At 200%
  /// the tile is twice the element, so at most one highlight is on screen, and the 4W of travel per
  /// 1.5 s yields two passes per cycle — one per 750 ms ON AVERAGE, not on a metronome: the
  /// animation declares no timing function, so <c>ease</c> applies and the sweep decelerates almost
  /// to a stop at each cycle boundary (measured: 2.1%/s against a median of 221.7%/s).
  /// <para>
  /// ⚠ Reducing <c>background-size</c> does not simply mean "more bands", and the intuition that it
  /// scales with element width is wrong — it is a PERCENTAGE OF THE ELEMENT, so the tile:element
  /// ratio is identical on every shape. At exactly 100% the animation stops dead: percentage
  /// background-positions resolve against (areaWidth − imageWidth), which is zero, so every keyframe
  /// maps to the same offset. A second band needs 50% or below.
  /// </para>
  /// <para>
  /// ⚠ These assertions are whitespace-exact, deliberately — a bare <c>Contains("50%")</c> would
  /// match an unrelated percentage anywhere in the rule. If a formatter ever reflows this block,
  /// update the literals to match; do not weaken them.
  /// </para>
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
  /// <para>
  /// The single-declaration assertions close the one way this control could go vacuous: CSS is
  /// last-wins, so a SECOND <c>--surface-overlay:</c> later in <c>:root</c> would govern the app
  /// while <see cref="DeclaredValueOf"/> — which reads the first — went on reporting the old value.
  /// </para>
  /// <para>
  /// <c>--surface-raised</c> is pinned for a different reason: the owner did not approve a colour,
  /// they approved a <em>delta against <c>#141416</c></em> — 36 at the first two sittings, 16 at the
  /// third. Moving the base silently changes the quantity that was sighted, so both ends of that
  /// delta are held, not just the new one.
  /// </para>
  /// </remarks>
  [Fact]
  public async Task SkeletonShimmer_LeavesTheSharedSurfaceOverlayTokenWhereItWas()
  {
    var css = await _factory.CreateClient().GetStringAsync("/css/design-system.css");

    // Equality, not Contains: "--surface-overlay: color-mix(in srgb, #1A1A1D 20%, #38383F)"
    // CONTAINS #1A1A1D while re-theming all 22 consumers.
    Assert.Equal("#1A1A1D", DeclaredValueOf(css, "--surface-overlay"));
    Assert.DoesNotContain("var(--surface-overlay)", SkeletonLoadingPrimitive(css));

    // Exactly one declaration each. CSS is last-wins and DeclaredValueOf reads the FIRST match, so
    // a second `--surface-overlay:` appended anywhere later would govern the app while the
    // assertion above went on cheerfully reporting the old value.
    Assert.Equal(1, DeclarationCount(css, "--surface-overlay"));
    Assert.Equal(1, DeclarationCount(css, "--skeleton-shimmer-highlight"));

    // The shimmer's two outer stops are --surface-raised, so it is the other end of the delta 36
    // the owner sighted. Both stops of the judged gradient are pinned, not just the highlight.
    Assert.Equal("#141416", DeclaredValueOf(css, "--surface-raised"));
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
  /// <para>
  /// ⚠ The override beats the primitive by SOURCE ORDER alone. A media query adds no specificity
  /// and there is no <c>!important</c>, so the two rules are (0,1,0) against (0,1,0) and the later
  /// one wins purely by position. That is worth an assertion rather than a comment: this file's own
  /// section numbering is already out of order — §26 Accessibility sits before §18, and there are
  /// two different §18s — so a "put the sections back in order" pass is a live possibility, and it
  /// would silently restore the gradient for reduced-motion users with the whole suite green.
  /// </para>
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

    // Equal specificity and no !important, so the override wins on source order and nothing else.
    Assert.True(
      overrideStart > css.IndexOf(".skeleton-loading {", StringComparison.Ordinal),
      "the reduced-motion override must come AFTER the primitive or it loses the cascade");
  }

  /// <summary>
  /// Returns the body of the shimmer primitive — the FIRST <c>.skeleton-loading {</c> in the file,
  /// which is what the primitive is today (the reduced-motion override of the same selector sits
  /// further down).
  /// </summary>
  /// <remarks>
  /// ⚠ The selection is BY POSITION. The <c>linear-gradient</c> assertion below is a tripwire, not
  /// a search criterion — it runs after the slice has already been chosen, so if the reduced-motion
  /// block ever moved above the primitive this helper would slice the wrong rule and fail loudly
  /// rather than quietly find the right one. Loud is the point; do not "fix" it by making the
  /// assertion conditional.
  /// </remarks>
  private static string SkeletonLoadingPrimitive(string css)
  {
    var start = css.IndexOf(".skeleton-loading {", StringComparison.Ordinal);
    Assert.True(start >= 0, "the .skeleton-loading primitive is gone or was renamed");

    var rule = css[start..css.IndexOf('}', start)];
    Assert.Contains("linear-gradient(90deg", rule);
    return rule;
  }

  /// <summary>
  /// Returns the declared value of a custom property — everything between the colon and the
  /// semicolon, trimmed — so callers can assert on the value with <c>Assert.Equal</c> without also
  /// asserting the column the file happens to pad it to.
  /// </summary>
  /// <remarks>
  /// Returns the FIRST match. That is only authoritative while the property is declared once, which
  /// is why callers pair this with <see cref="DeclarationCount"/>: CSS is last-wins, so a second
  /// declaration further down would govern the app while this went on reporting the first.
  /// </remarks>
  private static string DeclaredValueOf(string css, string property)
  {
    var start = css.IndexOf(property + ":", StringComparison.Ordinal);
    Assert.True(start >= 0, $"{property} is not declared");

    var valueStart = start + property.Length + 1;
    return css[valueStart..css.IndexOf(';', valueStart)].Trim();
  }

  /// <summary>
  /// How many times a custom property is DECLARED (<c>--name:</c>), as opposed to consumed
  /// (<c>var(--name)</c>) or merely mentioned in prose. Used to prove a token has exactly one
  /// declaration, which is what makes <see cref="DeclaredValueOf"/>'s first-match read authoritative
  /// rather than merely first.
  /// </summary>
  private static int DeclarationCount(string css, string property)
  {
    var needle = property + ":";
    var count = 0;

    for (var i = css.IndexOf(needle, StringComparison.Ordinal); i >= 0;
         i = css.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
    {
      count++;
    }

    return count;
  }
}
