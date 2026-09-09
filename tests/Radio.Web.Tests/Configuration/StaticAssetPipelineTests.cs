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
}
