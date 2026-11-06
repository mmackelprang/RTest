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

  /// <summary>A finger presses the thumb, where the room's volume is. See SeekBar's relative drag.</summary>
  private static Task PressThumbAsync(IRenderedComponent<HouseZoneCard> cut, SeekBar bar, double fraction) =>
    cut.InvokeAsync(() => bar.OnDragMove(fraction));

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

    await PressThumbAsync(cut, bar, 0.4);
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

    await PressThumbAsync(cut, bar, 0.4);
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
  [InlineData(0.90, 81, 80)]    // the route floors, the zone clamps to 81, and the amp packet makes that 80
  public async Task Card_Volume_IsWhatTheControllerWouldKeep(double fraction, int maxVolume, int expected)
  {
    List<int> committed = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(maxVolume: maxVolume), onVolume: committed.Add);
    SeekBar bar = cut.FindComponent<SeekBar>().Instance;

    await PressThumbAsync(cut, bar, 0.4);
    await cut.InvokeAsync(() => bar.OnDragMove(fraction));
    await cut.InvokeAsync(() => bar.OnDragEnd(fraction));

    committed.Should().Equal(expected);
  }

  [Fact]
  public async Task Card_Dragging_SendsOneVolume_OnRelease()
  {
    List<int> committed = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(volume: 40), onVolume: committed.Add);
    SeekBar bar = cut.FindComponent<SeekBar>().Instance;

    await PressThumbAsync(cut, bar, 0.4);
    foreach (double fraction in new[] { 0.42, 0.44, 0.47, 0.51, 0.49, 0.48 })
    {
      await cut.InvokeAsync(() => bar.OnDragMove(fraction));
    }

    committed.Should().BeEmpty("nothing is sent until the finger lifts");

    await cut.InvokeAsync(() => bar.OnDragEnd(0.46));

    committed.Should().Equal(46);
  }

  // ⚠ The owner's ruling: a tap must not jump a room's volume. A tap reaches the bar as a press and a
  // release at one place, far from the thumb here.
  [Theory]
  [InlineData(0.95, 0.95)]
  [InlineData(0.95, 0.96)]    // a finger is never quite still
  [InlineData(0.05, 0.05)]
  public async Task Card_TapOnTheTrack_SendsNothing_AndTheShownValueDoesNotChange(double press, double release)
  {
    List<int> committed = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(volume: 40), onVolume: committed.Add);
    SeekBar bar = cut.FindComponent<SeekBar>().Instance;

    await cut.InvokeAsync(() => bar.OnDragMove(press));
    cut.Find("[data-testid='house-zone-level']").TextContent.Should().Contain("40");
    cut.Find(".seek-bar-fill").GetAttribute("style").Should().Contain("40%");

    await cut.InvokeAsync(() => bar.OnDragEnd(release));

    committed.Should().BeEmpty();
    cut.Find("[data-testid='house-zone-level']").TextContent.Should().Contain("40");
    cut.Find(".seek-bar-fill").GetAttribute("style").Should().Contain("40%");
  }

  [Fact]
  public async Task Card_DragStartedOnTheTrack_MovesByTheFingersTravel_WithoutJumping()
  {
    List<int> committed = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(volume: 40), onVolume: committed.Add);
    SeekBar bar = cut.FindComponent<SeekBar>().Instance;

    await cut.InvokeAsync(() => bar.OnDragMove(0.90));
    await cut.InvokeAsync(() => bar.OnDragMove(0.80));
    cut.Find("[data-testid='house-zone-level']").TextContent.Should().Contain("30");

    await cut.InvokeAsync(() => bar.OnDragEnd(0.70));

    committed.Should().Equal(20);
  }

  [Fact]
  public async Task Card_RoomGoingOffDuringADrag_SendsNothingOnRelease()
  {
    List<int> committed = [];
    IRenderedComponent<HouseZoneCard> cut = Render(Zone(volume: 40), onVolume: committed.Add);
    SeekBar bar = cut.FindComponent<SeekBar>().Instance;
    await PressThumbAsync(cut, bar, 0.4);
    await cut.InvokeAsync(() => bar.OnDragMove(0.6));

    cut.SetParametersAndRender(parameters => parameters.Add(card => card.Zone, Zone(power: false, volume: 40)));
    await cut.InvokeAsync(() => bar.OnDragEnd(0.6));

    committed.Should().BeEmpty("a volume must not reach a room that went off under the finger");

    // Nothing of the drag is left when the room comes back.
    cut.SetParametersAndRender(parameters => parameters.Add(card => card.Zone, Zone(volume: 40)));
    cut.Find("[data-testid='house-zone-level']").TextContent.Should().Contain("40");
    cut.Find(".seek-bar").ClassList.Should().NotContain("is-dragging");
  }

  [Fact]
  public void Card_BeingTuned_IsMarked()
  {
    Render(Zone()).Find("[data-testid='house-zone-card']").ClassList.Should().NotContain("is-tuned");

    IRenderedComponent<HouseZoneCard> cut = RenderComponent<HouseZoneCard>(parameters => parameters
      .Add(card => card.Zone, Zone())
      .Add(card => card.Tuned, true));

    cut.Find("[data-testid='house-zone-card']").ClassList.Should().Contain("is-tuned");
  }
}
