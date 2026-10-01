using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the shared <see cref="PresetCard"/> component — the single
/// saved-station renderer (Proposal A, HANDOFF-saved-station-display). One
/// component, two variants (Bar = the Home radio panel's preset bar, UI-20;
/// Card = Radio page 480px card) so the two surfaces can't drift.
///
/// Contract under test:
///   - Bar + name → .rcp-bar-card-name carries the name; the meta line reads
///     "01 · FM 90.30" — ordinal, band, unit-less frequency.
///   - Bar + no name → the frequency is the primary line and the meta line drops it.
///   - Bar has no kebab (owner decision Q3); its aria label speaks the unit.
///   - Card + name → .preset-card-name carries the name, .preset-card-freq the
///     unit-less value.
///   - IsActive → root carries .is-active (both variants); Bar adds aria-current.
///   - OnSelect fires with the PresetId on click (both variants).
/// </summary>
public class PresetCardTests : TestContext
{
  public PresetCardTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  [Fact]
  public void Bar_WithName_RendersNameThenOrdinalBandAndUnitlessFreq()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Bar)
      .Add(x => x.PresetId, "p1")
      .Add(x => x.Name, "KEXP Seattle")
      .Add(x => x.Frequency, 90_300_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.SlotNumber, 1));

    cut.Find(".rcp-bar-card-name").TextContent.Trim().Should().Be("KEXP Seattle");
    var meta = cut.Find(".rcp-bar-card-meta").TextContent.Trim();
    meta.Should().Be("01 · FM 90.30");
    meta.Should().NotContain("MHz", "the card face carries the value; the unit is in the tooltip and aria label");
  }

  [Fact]
  public void Bar_NoName_PromotesFreqToPrimaryLine_AndDropsItFromTheMeta()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Bar)
      .Add(x => x.PresetId, "p2")
      .Add(x => x.Name, "   ")
      .Add(x => x.Frequency, 88_500_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.SlotNumber, 3));

    cut.Find(".rcp-bar-card-name.rcp-bar-card-name-freq").TextContent.Trim().Should().Be("88.50");
    cut.Find(".rcp-bar-card-meta").TextContent.Trim().Should().Be("03 · FM");
  }

  [Fact]
  public void Bar_Title_CarriesFullNameAndUnit()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Bar)
      .Add(x => x.PresetId, "p3")
      .Add(x => x.Name, "Classic Vinyl Rock Channel")
      .Add(x => x.Frequency, 105_100_000)
      .Add(x => x.Band, "FM"));

    var title = cut.Find(".rcp-bar-card").GetAttribute("title") ?? string.Empty;
    title.Should().Contain("Classic Vinyl Rock Channel", "a name past two lines is clamped visually but the tooltip has all of it");
    title.Should().Contain("105.10 MHz", "the tooltip carries the full unit even though the card face drops it");
  }

  [Fact]
  public void Bar_AriaLabel_SpeaksOrdinalNameAndUnit_AndNowPlayingWhenActive()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Bar)
      .Add(x => x.PresetId, "p4")
      .Add(x => x.Name, "Rock 92.3")
      .Add(x => x.Frequency, 92_300_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.SlotNumber, 6)
      .Add(x => x.IsActive, true));

    var card = cut.Find(".rcp-bar-card");
    card.GetAttribute("aria-label").Should().Be("Preset 6, Rock 92.3, FM 92.30 megahertz, now playing");
    card.GetAttribute("aria-current").Should().Be("true");
  }

  [Fact]
  public void Bar_AmAriaLabel_SpeaksKilohertz_AndInactiveCarriesNoAriaCurrent()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Bar)
      .Add(x => x.PresetId, "p5")
      .Add(x => x.Name, "AM 1010")
      .Add(x => x.Frequency, 1_010_000)
      .Add(x => x.Band, "AM")
      .Add(x => x.SlotNumber, 1));

    var card = cut.Find(".rcp-bar-card");
    card.GetAttribute("aria-label").Should().Be("Preset 1, AM 1010, AM 1010 kilohertz");
    card.HasAttribute("aria-current").Should().BeFalse();
  }

  [Fact]
  public void Bar_HasNoKebab()
  {
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Bar)
      .Add(x => x.PresetId, "p6")
      .Add(x => x.Name, "KEXP")
      .Add(x => x.Frequency, 90_300_000)
      .Add(x => x.Band, "FM"));

    cut.FindAll("button").Should().BeEmpty();
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
  [InlineData(PresetCardVariant.Bar, "rcp-bar-card")]
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
  public void Bar_OnSelect_FiresWithPresetId()
  {
    string? selected = null;
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Bar)
      .Add(x => x.PresetId, "sel-bar")
      .Add(x => x.Name, "KEXP")
      .Add(x => x.Frequency, 90_300_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.OnSelect, (string id) => { selected = id; }));

    cut.Find(".rcp-bar-card").Click();

    selected.Should().Be("sel-bar");
  }

  [Fact]
  public void Bar_Enter_FiresOnSelect_OtherKeysDoNot()
  {
    var selected = new List<string>();
    var cut = RenderComponent<PresetCard>(p => p
      .Add(x => x.Variant, PresetCardVariant.Bar)
      .Add(x => x.PresetId, "kbd")
      .Add(x => x.Name, "KEXP")
      .Add(x => x.Frequency, 90_300_000)
      .Add(x => x.Band, "FM")
      .Add(x => x.OnSelect, (string id) => { selected.Add(id); }));

    cut.Find(".rcp-bar-card").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "a" });
    selected.Should().BeEmpty();
    cut.Find(".rcp-bar-card").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
    selected.Should().Equal("kbd");
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
