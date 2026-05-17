using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the <see cref="ConfidencePips"/> widget introduced by PR 2 of
/// the Radio Controller Polish arc. Asserts the lit-pip count for each
/// <see cref="ConfidenceBucket"/> value, the word label that replaces the
/// banned raw percentage ("80%"), and the <c>data-bucket</c> attribute that the
/// design-system stylesheet hooks into for the per-bucket colour rules.
/// </summary>
public class ConfidencePipsTests : TestContext
{
  public ConfidencePipsTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void Strong_LightsThreePips()
  {
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, ConfidenceBucket.Strong));

    var pips = cut.FindAll(".confidence-pip");
    Assert.Equal(3, pips.Count);
    var lit = cut.FindAll(".confidence-pip.is-lit");
    Assert.Equal(3, lit.Count);
  }

  [Fact]
  public void Likely_LightsTwoPips()
  {
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, ConfidenceBucket.Likely));

    var lit = cut.FindAll(".confidence-pip.is-lit");
    Assert.Equal(2, lit.Count);
    var unlit = cut.FindAll(".confidence-pip:not(.is-lit)");
    Assert.Single(unlit);
  }

  [Fact]
  public void Possible_LightsOnePip()
  {
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, ConfidenceBucket.Possible));

    var lit = cut.FindAll(".confidence-pip.is-lit");
    Assert.Single(lit);
    var unlit = cut.FindAll(".confidence-pip:not(.is-lit)");
    Assert.Equal(2, unlit.Count);
  }

  [Fact]
  public void None_LightsZeroPips_AndLabelReadsNoMatch()
  {
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, ConfidenceBucket.None));

    var lit = cut.FindAll(".confidence-pip.is-lit");
    Assert.Empty(lit);
    Assert.Equal("No match", cut.Find(".confidence-label").TextContent);
  }

  [Fact]
  public void Strong_BucketLabelIsStrong()
  {
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, ConfidenceBucket.Strong));

    Assert.Equal("Strong", cut.Find(".confidence-label").TextContent);
  }

  [Fact]
  public void Likely_BucketLabelIsLikely()
  {
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, ConfidenceBucket.Likely));

    Assert.Equal("Likely", cut.Find(".confidence-label").TextContent);
  }

  [Fact]
  public void Possible_BucketLabelIsPossible()
  {
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, ConfidenceBucket.Possible));

    Assert.Equal("Possible", cut.Find(".confidence-label").TextContent);
  }

  [Theory]
  [InlineData(ConfidenceBucket.Strong, "strong")]
  [InlineData(ConfidenceBucket.Likely, "likely")]
  [InlineData(ConfidenceBucket.Possible, "possible")]
  [InlineData(ConfidenceBucket.None, "none")]
  public void DataBucketAttribute_PropagatesEnumNameLowercased(ConfidenceBucket bucket, string expected)
  {
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, bucket));

    var root = cut.Find(".confidence-pips");
    Assert.Equal(expected, root.GetAttribute("data-bucket"));
  }

  [Theory]
  [InlineData(ConfidenceBucket.Strong)]
  [InlineData(ConfidenceBucket.Likely)]
  [InlineData(ConfidenceBucket.Possible)]
  [InlineData(ConfidenceBucket.None)]
  public void RenderedMarkup_ContainsNoRawPercentage(ConfidenceBucket bucket)
  {
    // Regression guard: PR 2's headline acceptance criterion bans the raw
    // percentage text from the recognition surface. The widget that REPLACES
    // those percentages must itself never emit one.
    var cut = RenderComponent<ConfidencePips>(p => p
      .Add(x => x.Bucket, bucket));

    Assert.DoesNotMatch(@"\b\d{1,3}\s?%", cut.Markup);
  }
}
