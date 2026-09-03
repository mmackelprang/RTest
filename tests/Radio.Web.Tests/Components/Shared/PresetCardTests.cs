using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the shared <see cref="PresetCard"/> component — the single
/// saved-station renderer (Proposal A, HANDOFF-saved-station-display). One
/// component, two variants (Rail = Home PRESETS rail compact row; Card = Radio
/// page 480px card) so the two surfaces can't drift.
///
/// Contract under test:
///   - Rail + name → .rcp-preset-name carries the name, .rcp-preset-freq the
///     unit-less frequency value (no "MHz" in the row face).
///   - Rail + no name → .rcp-preset-name.rcp-preset-name-freq promotes the
///     frequency to the primary line; the freq tail is empty.
///   - Card + name → .preset-card-name carries the name, .preset-card-freq the
///     unit-less value.
///   - IsActive → root carries .is-active (both variants).
///   - OnSelect fires with the PresetId on click (both variants).
///   - OnKebab fires on the rail kebab; rail row title carries the full name +
///     full MHz/kHz unit.
/// </summary>
public class PresetCardTests : TestContext
{
  public PresetCardTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void Rail_WithName_RendersNameAndUnitlessFreq()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Rail)
      .Add(x => x.PresetId, "p1")
      .Add(x => x.Name, "KEXP Seattle")
      .Add(x => x.Frequency, 90_300_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.SlotNumber, 1));

    cut.Find(".rcp-preset-name").TextContent.Trim().Should().Be("KEXP Seattle");
    // Unit-less value in the row face: "90.30", NOT "90.30 MHz".
    var freq = cut.Find(".rcp-preset-freq").TextContent.Trim();
    freq.Should().Be("90.30");
    freq.Should().NotContain("MHz");
  }

  [Fact]
  public void Rail_NoName_PromotesFreqToPrimaryLine()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Rail)
      .Add(x => x.PresetId, "p2")
      .Add(x => x.Name, "   ")
      .Add(x => x.Frequency, 88_500_000)
      .Add(x => x.Band, "FM"));

    // The primary line carries the freq with the promotion class.
    var primary = cut.Find(".rcp-preset-name.rcp-preset-name-freq");
    primary.TextContent.Trim().Should().Be("88.50");

    // The dim freq tail is present but empty (keeps the 4-column grid intact).
    cut.Find(".rcp-preset-freq").TextContent.Trim().Should().BeEmpty();
  }

  [Fact]
  public void Rail_RowTitle_CarriesFullNameAndUnit()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Rail)
      .Add(x => x.PresetId, "p3")
      .Add(x => x.Name, "Classic Vinyl Rock Channel")
      .Add(x => x.Frequency, 105_100_000)
      .Add(x => x.Band, "FM"));

    var title = cut.Find(".rcp-preset-item").GetAttribute("title") ?? string.Empty;
    title.Should().Contain("Classic Vinyl Rock Channel", "long names truncate visually but the tooltip has the full name");
    title.Should().Contain("105.10 MHz", "the tooltip carries the full unit even though the row face drops it");
  }

  [Fact]
  public void Rail_AmRowTitle_UsesKhzUnit()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Rail)
      .Add(x => x.PresetId, "p4")
      .Add(x => x.Name, "AM 1010")
      .Add(x => x.Frequency, 1_010_000)
      .Add(x => x.Band, "AM"));

    cut.Find(".rcp-preset-freq").TextContent.Trim().Should().Be("1010");
    (cut.Find(".rcp-preset-item").GetAttribute("title") ?? string.Empty)
      .Should().Contain("1010 kHz");
  }

  [Fact]
  public void Card_WithName_RendersNameAndUnitlessFreq()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Card)
      .Add(x => x.PresetId, "c1")
      .Add(x => x.Name, "KQED Public Radio")
      .Add(x => x.Frequency, 88_500_000)
      .Add(x => x.Band, "FM"));

    cut.Find(".preset-card-name").TextContent.Trim().Should().Be("KQED Public Radio");
    cut.Find(".preset-card-freq").TextContent.Trim().Should().Be("88.50");
  }

  [Fact]
  public void Card_NoName_PromotesFreqToPrimaryLine()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Card)
      .Add(x => x.PresetId, "c2")
      .Add(x => x.Name, null)
      .Add(x => x.Frequency, 98_500_000)
      .Add(x => x.Band, "FM"));

    cut.Find(".preset-card-name.preset-card-name-freq").TextContent.Trim().Should().Be("98.50");
    // No-name card has no separate dim freq tail.
    cut.FindAll(".preset-card-freq").Should().BeEmpty();
  }

  [Theory]
  [InlineData(PresetCardVariant.Rail, "rcp-preset-item")]
  [InlineData(PresetCardVariant.Card, "preset-card")]
  public void IsActive_AddsActiveClass(PresetCardVariant variant, string rootClass)
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, variant)
      .Add(x => x.PresetId, "a1")
      .Add(x => x.Name, "Active Station")
      .Add(x => x.Frequency, 90_300_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.IsActive, true));

    cut.Find($".{rootClass}").ClassList.Should().Contain("is-active");
  }

  [Fact]
  public void Rail_OnSelect_FiresWithPresetId()
  {
    string? selected = null;
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Rail)
      .Add(x => x.PresetId, "sel-rail")
      .Add(x => x.Name, "KEXP")
      .Add(x => x.Frequency, 90_300_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.OnSelect, (string id) => { selected = id; }));

    cut.Find(".rcp-preset-item").Click();

    selected.Should().Be("sel-rail");
  }

  [Fact]
  public void Card_OnSelect_FiresWithPresetId()
  {
    string? selected = null;
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Card)
      .Add(x => x.PresetId, "sel-card")
      .Add(x => x.Name, "KQED")
      .Add(x => x.Frequency, 88_500_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.OnSelect, (string id) => { selected = id; }));

    cut.Find(".preset-card").Click();

    selected.Should().Be("sel-card");
  }

  [Fact]
  public void Rail_Kebab_FiresOnKebabWithPresetId()
  {
    string? kebabId = null;
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Rail)
      .Add(x => x.PresetId, "keb-1")
      .Add(x => x.Name, "KEXP")
      .Add(x => x.Frequency, 90_300_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.OnKebab, (string id) => { kebabId = id; }));

    cut.Find(".rcp-preset-kebab").Click();

    kebabId.Should().Be("keb-1");
  }

  [Fact]
  public void Card_OmitsDeleteButton_WhenNoDeleteDelegate()
  {
    // The Card variant only renders the delete button when OnDelete is wired.
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Card)
      .Add(x => x.PresetId, "nodel")
      .Add(x => x.Name, "KQED")
      .Add(x => x.Frequency, 88_500_000)
      .Add(x => x.Band, "FM"));

    cut.FindAll("button.rz-button").Should().BeEmpty();
  }
}
