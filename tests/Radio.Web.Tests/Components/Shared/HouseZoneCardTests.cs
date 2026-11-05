using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Radio.Web.Components.Shared;
using Radio.Web.Components.Shared.HouseAudio;
using Radio.Web.Models;

namespace Radio.Web.Tests.Components.Shared;

public class HouseZoneCardTests : TestContext
{
  private static readonly IReadOnlyList<HouseSource> Sources =
  [
    new HouseSource(0, "Audiocast 1", 7),
    new HouseSource(1, "Audiocast 2", 7),
    new HouseSource(2, "Pi Audio", 11)
  ];

  public HouseZoneCardTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private static HouseZone Zone(
    bool power = true, bool muted = false, int volume = 40, int sourceId = 0, int maxVolume = 100) =>
    new(0, 0, "Screen Porch", power, volume, sourceId, muted, maxVolume, 0, 0, false, 0, 30, false, 0);

  private IRenderedComponent<HouseZoneCard> Render(
    HouseZone zone,
    Action? onPower = null,
    Action? onMute = null,
    Action<int>? onVolume = null,
    Action<int>? onSource = null,
    Action? onTune = null,
    bool stale = false) =>
    RenderComponent<HouseZoneCard>(parameters => parameters
      .Add(card => card.Zone, zone)
      .Add(card => card.Sources, Sources)
      .Add(card => card.Stale, stale)
      .Add(card => card.OnPower, EventCallback.Factory.Create(this, onPower ?? (() => { })))
      .Add(card => card.OnMute, EventCallback.Factory.Create(this, onMute ?? (() => { })))
      .Add(card => card.OnVolumeCommitted, EventCallback.Factory.Create(this, onVolume ?? (_ => { })))
      .Add(card => card.OnSource, EventCallback.Factory.Create(this, onSource ?? (_ => { })))
      .Add(card => card.OnTune, EventCallback.Factory.Create(this, onTune ?? (() => { }))));

  [Theory]
  [InlineData(true, "On", "true")]
  [InlineData(false, "Off", "false")]
  public void Card_PowerButton_IsOneTouch_AndShowsOnOrOff(bool power, string label, string pressed)
  {
    int presses = 0;
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(power: power), onPower: () => presses++);

    AngleSharp.Dom.IElement button = cut.Find("[data-testid='house-zone-power']");
    button.TextContent.Should().Contain(label);
    button.GetAttribute("aria-pressed").Should().Be(pressed);

    button.Click();

    presses.Should().Be(1, "one touch silences a room, with nothing to confirm");
  }

  [Theory]
  [InlineData(true, "true", "volume_off")]
  [InlineData(false, "false", "volume_up")]
  public void Card_MuteButton_IsOneTouch_AndShowsMutedState(bool muted, string pressed, string icon)
  {
    int presses = 0;
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(muted: muted), onMute: () => presses++);

    AngleSharp.Dom.IElement button = cut.Find("[data-testid='house-zone-mute']");
    button.GetAttribute("aria-pressed").Should().Be(pressed);
    button.TextContent.Should().Contain(icon);

    button.Click();

    presses.Should().Be(1);
  }

  [Fact]
  public void Card_RoomThatIsOff_StillOffersItsPowerButton_AndNothingElse()
  {
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(power: false));

    cut.Find("[data-testid='house-zone-power']").HasAttribute("disabled").Should().BeFalse();
    cut.Find("[data-testid='house-zone-mute']").HasAttribute("disabled").Should().BeTrue();
    cut.Find("[data-testid='house-zone-source']").HasAttribute("disabled").Should().BeTrue();
    cut.Find("[data-testid='house-zone-source']").TextContent.Should().Contain("Off");
    cut.Find(".seek-bar").GetAttribute("aria-disabled").Should().Be("true");
  }

  [Fact]
  public void Card_ShowsTheSourceName_AndThatItIsMuted()
  {
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(muted: true, sourceId: 2));

    cut.Find("[data-testid='house-zone-source']").TextContent.Should().Contain("Pi Audio").And.Contain("muted");
  }

  [Fact]
  public void Card_UnknownSource_ShowsItsInputNumber()
  {
    // sourceID is zero-based, and the owner's inputs are numbered from 1.
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(sourceId: 4));

    cut.Find("[data-testid='house-zone-source']").TextContent.Should().Contain("Input 5");
  }

  [Fact]
  public void Card_SourcePicker_ListsEverySource_AndOneTouchPicksIt()
  {
    List<int> picked = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(sourceId: 0), onSource: picked.Add);
    cut.FindAll("[data-testid='house-source-option']").Should().BeEmpty();

    cut.Find("[data-testid='house-zone-source']").Click();

    IReadOnlyList<AngleSharp.Dom.IElement> options = cut.FindAll("[data-testid='house-source-option']");
    options.Select(option => option.TextContent.Trim()).Should()
      .ContainInOrder("Audiocast 1", "Audiocast 2", "Pi Audio");

    options[2].Click();

    picked.Should().Equal(2);
    cut.FindAll("[data-testid='house-source-option']").Should().BeEmpty("picking closes the list");
  }

  [Fact]
  public void Card_SourcePicker_ThePlayingSource_SendsNothing()
  {
    List<int> picked = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(sourceId: 0), onSource: picked.Add);

    cut.Find("[data-testid='house-zone-source']").Click();
    cut.FindAll("[data-testid='house-source-option']")[0].Click();

    picked.Should().BeEmpty();
  }

  [Fact]
  public void Card_SourcePicker_TappingAway_ClosesIt()
  {
    IRenderedComponent<HouseZoneCard> cut = Render(Zone());
    cut.Find("[data-testid='house-zone-source']").Click();

    cut.Find("[data-testid='house-source-away']").Click();

    cut.FindAll("[data-testid='house-source-option']").Should().BeEmpty();
  }

  [Fact]
  public void Card_Tune_FiresOnTune()
  {
    int presses = 0;
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(), onTune: () => presses++);

    cut.Find("[data-testid='house-zone-tune']").Click();

    presses.Should().Be(1);
  }

  [Fact]
  public void Card_Stale_SaysLastKnown()
  {
    Render(Zone()).FindAll("[data-testid='house-zone-stale']").Should().BeEmpty();
    Render(Zone(), stale: true).Find("[data-testid='house-zone-stale']").TextContent.Should().Contain("Last known");
  }

  [Fact]
  public async Task Card_Drag_ShowsTheFingersVolume_AndSendsNothingUntilRelease()
  {
    List<int> committed = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(volume: 40), onVolume: committed.Add);
    SeekBar bar = cut.FindComponent<SeekBar>().Instance;

    await cut.InvokeAsync(() => bar.OnDragMove(0.5));
    await cut.InvokeAsync(() => bar.OnDragMove(0.6));

    cut.Find("[data-testid='house-zone-level']").TextContent.Should().Contain("60");
    committed.Should().BeEmpty();

    await cut.InvokeAsync(() => bar.OnDragEnd(0.46));

    committed.Should().Equal(46);
  }

  [Fact]
  public async Task Card_WhileDragging_IgnoresIncomingVolume()
  {
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(volume: 40));
    SeekBar bar = cut.FindComponent<SeekBar>().Instance;

    await cut.InvokeAsync(() => bar.OnDragMove(0.6));
    cut.SetParametersAndRender(parameters => parameters.Add(card => card.Zone, Zone(volume: 20)));

    cut.Find("[data-testid='house-zone-level']").TextContent.Should().Contain("60");
    cut.Find(".seek-bar-fill").GetAttribute("style").Should().Contain("60%");

    // A cancelled drag sends nothing, and hands the card back to the reported state.
    await cut.InvokeAsync(() => bar.OnDragCancel());

    cut.Find("[data-testid='house-zone-level']").TextContent.Should().Contain("20");
    cut.Find(".seek-bar-fill").GetAttribute("style").Should().Contain("20%");
  }

  [Fact]
  public void Card_SourcePicker_ThatWasOpen_StaysClosed_WhenTheRoomComesBackOn()
  {
    IRenderedComponent<HouseZoneCard> cut = Render(Zone());
    cut.Find("[data-testid='house-zone-source']").Click();

    cut.SetParametersAndRender(parameters => parameters.Add(card => card.Zone, Zone(power: false)));
    cut.FindAll("[data-testid='house-source-option']").Should().BeEmpty();

    cut.SetParametersAndRender(parameters => parameters.Add(card => card.Zone, Zone(power: true)));

    cut.FindAll("[data-testid='house-source-option']").Should().BeEmpty("nobody asked for it this time");
    cut.FindAll("[data-testid='house-source-away']").Should().BeEmpty();
  }

  [Theory]
  [InlineData(0.47, 80, 46)]    // the controller floors to even, so what the card shows is what lands
  [InlineData(0.90, 80, 80)]    // and clamps to the room's maximum
  [InlineData(0.90, 81, 80)]    // and an odd maximum floors too, as the route does
  public async Task Card_Volume_IsWhatTheControllerWouldKeep(double fraction, int maxVolume, int expected)
  {
    List<int> committed = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(maxVolume: maxVolume), onVolume: committed.Add);
    SeekBar bar = cut.FindComponent<SeekBar>().Instance;

    await cut.InvokeAsync(() => bar.OnDragMove(fraction));
    await cut.InvokeAsync(() => bar.OnDragEnd(fraction));

    committed.Should().Equal(expected);
  }
}
