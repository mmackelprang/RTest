using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Radio.Web.Components.Shared;
using Radio.Web.Components.Shared.HouseAudio;
using Radio.Web.Models;

namespace Radio.Web.Tests.Components.Shared;

public class HouseZoneSettingsPopoverTests : TestContext
{
  private readonly List<HouseZoneSetting> _settings = [];
  private readonly List<int> _maxVolumes = [];
  private int _deletes;
  private int _closes;

  public HouseZoneSettingsPopoverTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private static HouseZone Zone(
    int bass = 0, int treble = 0, int balance = 0, bool loudness = false, bool doNotDisturb = false,
    int partyMode = 0, int turnOnVolume = 30, int maxVolume = 80) =>
    new(0, 0, "Screen Porch", true, 40, 0, false, maxVolume, bass, treble, loudness, balance, turnOnVolume,
      doNotDisturb, partyMode);

  private IRenderedComponent<HouseZoneSettingsPopover> Render(HouseZone zone, string? error = null, bool stale = false) =>
    RenderComponent<HouseZoneSettingsPopover>(parameters => parameters
      .Add(popover => popover.Zone, zone)
      .Add(popover => popover.Error, error)
      .Add(popover => popover.Stale, stale)
      .Add(popover => popover.OnSetting, EventCallback.Factory.Create<HouseZoneSetting>(this, _settings.Add))
      .Add(popover => popover.OnMaxVolume, EventCallback.Factory.Create<int>(this, _maxVolumes.Add))
      .Add(popover => popover.OnDelete, EventCallback.Factory.Create(this, () => _deletes++))
      .Add(popover => popover.OnClose, EventCallback.Factory.Create(this, () => _closes++)));

  private static SeekBar Slider(IRenderedComponent<HouseZoneSettingsPopover> cut, string label) =>
    cut.FindComponents<SeekBar>()
      .Single(bar => bar.Find(".seek-bar").GetAttribute("aria-label") == $"Screen Porch {label}")
      .Instance;

  private static string Text(IRenderedComponent<HouseZoneSettingsPopover> cut, string testId) =>
    cut.Find($"[data-testid='{testId}']").TextContent.Trim();

  [Theory]
  [InlineData("house-party-master", 2)]
  [InlineData("house-party-on", 1)]
  public void Popover_PartyMode_SendsParameter7_WithTheValue(string segment, int value)
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(partyMode: 0));

    cut.Find($"[data-testid='{segment}']").Click();

    _settings.Should().Equal(new HouseZoneSetting(HouseZoneParameter.PartyMode, value));
    ((int)HouseZoneParameter.PartyMode).Should().Be(7);
  }

  [Fact]
  public void Popover_PartyMode_TheSelectedSegment_SendsNothing()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(partyMode: 1));

    cut.Find("[data-testid='house-party-on']").Click();

    _settings.Should().BeEmpty();
  }

  [Theory]
  [InlineData(0, "house-party-off")]
  [InlineData(1, "house-party-on")]
  [InlineData(2, "house-party-master")]
  public void Popover_ShowsTheReportedPartyMode(int partyMode, string selected)
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(partyMode: partyMode));

    cut.FindAll("[data-testid^='house-party-']")
      .Where(segment => segment.GetAttribute("aria-pressed") == "true")
      .Select(segment => segment.GetAttribute("data-testid"))
      .Should().Equal(selected);
  }

  [Theory]
  [InlineData(false, 1)]
  [InlineData(true, 0)]
  public void Popover_Loudness_SendsParameter2(bool loudness, int value)
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(loudness: loudness));
    AngleSharp.Dom.IElement toggle = cut.Find("[data-testid='house-loudness']");
    toggle.GetAttribute("aria-pressed").Should().Be(loudness ? "true" : "false");

    toggle.Click();

    _settings.Should().Equal(new HouseZoneSetting(HouseZoneParameter.Loudness, value));
    ((int)HouseZoneParameter.Loudness).Should().Be(2);
  }

  [Theory]
  [InlineData(false, 1)]
  [InlineData(true, 0)]
  public void Popover_DoNotDisturb_SendsParameter6(bool doNotDisturb, int value)
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(doNotDisturb: doNotDisturb));
    AngleSharp.Dom.IElement toggle = cut.Find("[data-testid='house-dnd']");
    toggle.GetAttribute("aria-pressed").Should().Be(doNotDisturb ? "true" : "false");

    toggle.Click();

    _settings.Should().Equal(new HouseZoneSetting(HouseZoneParameter.DoNotDisturb, value));
    ((int)HouseZoneParameter.DoNotDisturb).Should().Be(6);
  }

  [Theory]
  [InlineData("house-bass-up", 3, 4)]
  [InlineData("house-bass-down", 3, 2)]
  [InlineData("house-bass-up", 9, 10)]
  [InlineData("house-bass-down", -9, -10)]
  public void Popover_Bass_StepsByOne_SendingParameter0(string button, int bass, int expected)
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(bass: bass));

    cut.Find($"[data-testid='{button}']").Click();

    _settings.Should().Equal(new HouseZoneSetting(HouseZoneParameter.Bass, expected));
    ((int)HouseZoneParameter.Bass).Should().Be(0);
  }

  [Theory]
  [InlineData("house-bass-up", 10)]
  [InlineData("house-bass-down", -10)]
  public void Popover_Bass_ClampsToMinus10To10(string button, int bass)
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(bass: bass));

    cut.Find($"[data-testid='{button}']").HasAttribute("disabled").Should().BeTrue();

    // A reported value past the end comes back into range, rather than stepping further out.
    cut.SetParametersAndRender(parameters => parameters.Add(popover => popover.Zone, Zone(bass: bass * 2)));
    string opposite = button.EndsWith("up", StringComparison.Ordinal) ? "house-bass-down" : "house-bass-up";
    cut.Find($"[data-testid='{opposite}']").Click();

    _settings.Should().Equal(new HouseZoneSetting(HouseZoneParameter.Bass, bass));
  }

  [Theory]
  [InlineData("house-treble-up", HouseZoneParameter.Treble, 1, 5)]
  [InlineData("house-balance-down", HouseZoneParameter.Balance, 3, -2)]
  public void Popover_TrebleAndBalance_SendTheirOwnParameter(string button, HouseZoneParameter parameter, int id, int expected)
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(treble: 4, balance: -1));

    cut.Find($"[data-testid='{button}']").Click();

    _settings.Should().Equal(new HouseZoneSetting(parameter, expected));
    ((int)parameter).Should().Be(id);
  }

  [Fact]
  public void Popover_ToneValues_ShowTheirSign()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(bass: -3, treble: 7, balance: 0));

    Text(cut, "house-bass-value").Should().Be("-3");
    Text(cut, "house-treble-value").Should().Be("+7");
    Text(cut, "house-balance-value").Should().Be("0");
  }

  // ⚠ The owner's ruling: a tap must not jump a value. This is the popover's half of it.
  [Theory]
  [InlineData("Turn On Volume")]
  [InlineData("Maximum Volume")]
  public async Task Popover_TapOnASliderTrack_SendsNothing_AndChangesNothing(string label)
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(turnOnVolume: 30, maxVolume: 80));
    string before = cut.Markup;
    SeekBar bar = Slider(cut, label);

    await cut.InvokeAsync(() => bar.OnDragMove(0.95));
    await cut.InvokeAsync(() => bar.OnDragEnd(0.95));

    _settings.Should().BeEmpty();
    _maxVolumes.Should().BeEmpty();
    cut.Markup.Should().Be(before);
  }

  [Fact]
  public async Task Popover_TurnOnVolume_Drag_SendsParameter4_OnRelease()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(turnOnVolume: 30));
    SeekBar bar = Slider(cut, "Turn On Volume");

    await cut.InvokeAsync(() => bar.OnDragMove(0.30));
    await cut.InvokeAsync(() => bar.OnDragMove(0.50));
    Text(cut, "house-turn-on-level").Should().Be("50");
    _settings.Should().BeEmpty();

    await cut.InvokeAsync(() => bar.OnDragEnd(0.55));

    // The amp holds this in steps of two, as it does the room's volume.
    _settings.Should().Equal(new HouseZoneSetting(HouseZoneParameter.TurnOnVolume, 54));
    ((int)HouseZoneParameter.TurnOnVolume).Should().Be(4);
  }

  [Fact]
  public async Task Popover_MaxVolume_Drag_SendsItsOwnCallback_OnRelease()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(maxVolume: 80));
    SeekBar bar = Slider(cut, "Maximum Volume");

    await cut.InvokeAsync(() => bar.OnDragMove(0.80));
    await cut.InvokeAsync(() => bar.OnDragMove(0.60));
    await cut.InvokeAsync(() => bar.OnDragEnd(0.60));

    _maxVolumes.Should().Equal(60);
    _settings.Should().BeEmpty();
  }

  [Fact]
  public async Task Popover_Slider_WhileDragging_IgnoresAReportedValue()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(turnOnVolume: 30));
    SeekBar bar = Slider(cut, "Turn On Volume");

    await cut.InvokeAsync(() => bar.OnDragMove(0.30));
    await cut.InvokeAsync(() => bar.OnDragMove(0.50));
    cut.SetParametersAndRender(parameters => parameters.Add(popover => popover.Zone, Zone(turnOnVolume: 10)));

    Text(cut, "house-turn-on-level").Should().Be("50");
  }

  [Fact]
  public void Popover_DeleteZone_AsksFirst()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone());

    cut.Find("[data-testid='house-delete-zone']").Click();

    _deletes.Should().Be(0, "the first tap only asks");
    cut.Find("[data-testid='house-delete-prompt']").TextContent.Should().Contain("Screen Porch");

    cut.Find("[data-testid='house-delete-confirm']").Click();

    _deletes.Should().Be(1);
  }

  [Fact]
  public void Popover_DeleteZone_Cancel_SendsNothing_AndReturnsToTheSettings()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone());
    cut.Find("[data-testid='house-delete-zone']").Click();

    cut.Find("[data-testid='house-delete-cancel']").Click();

    _deletes.Should().Be(0);
    cut.FindAll("[data-testid='house-delete-prompt']").Should().BeEmpty();
    cut.FindAll("[data-testid='house-party-on']").Should().ContainSingle();
  }

  [Fact]
  public void Popover_CloseAndTapAway_BothClose()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone());

    cut.Find("[data-testid='house-settings-close']").Click();
    cut.Find("[data-testid='house-settings-away']").Click();

    _closes.Should().Be(2);
  }

  [Fact]
  public void Popover_ShowsAFailure_WhereTheRoomNameWas()
  {
    IRenderedComponent<HouseZoneSettingsPopover> cut = Render(Zone(), error: "Screen Porch Bass was not changed.");

    Text(cut, "house-settings-error").Should().Contain("Bass was not changed");
  }

  [Fact]
  public void Popover_Stale_SaysLastKnown()
  {
    Render(Zone()).FindAll("[data-testid='house-settings-stale']").Should().BeEmpty();
    Render(Zone(), stale: true).Find("[data-testid='house-settings-stale']").TextContent.Should().Contain("Last known");
  }

  [Fact]
  public void Popover_HasNoTextInputs()
  {
    Render(Zone()).FindAll("input, textarea, select").Should().BeEmpty();
  }

  /// <summary>
  /// bUnit lays nothing out, so this holds the arithmetic the browser measurement rests on: the rows
  /// the popover renders are the rows its height pays for, and that height ends on the 720 px panel.
  /// A row added without paying for it fails here. The measurement itself is in the Task 9 report.
  /// </summary>
  [Fact]
  public void Popover_FitsThePanel_WithoutScrolling()
  {
    const int topBar = 120;
    const int topMargin = 8;
    const int row = 56;
    const int rowGap = 4;
    const int padding = 8;
    const int border = 1;
    const int panelHeight = 720;

    string css = File.ReadAllText(DesignSystemCssPath());
    Match height = Regex.Match(
      css, @"\.house-settings-popover \{[^}]*height: calc\((\d+) \* var\(--touch-preferred\)");
    height.Success.Should().BeTrue("the popover's height is stated in rows");
    int paidFor = int.Parse(height.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

    int rendered = Render(Zone()).FindAll("[data-testid='house-settings-popover'] > .house-settings-row").Count;

    rendered.Should().Be(paidFor);
    int bottom = topBar + topMargin + (rendered * row) + ((rendered - 1) * rowGap) + (2 * padding) + (2 * border);
    bottom.Should().BeLessThanOrEqualTo(panelHeight);
  }

  private static string DesignSystemCssPath()
  {
    for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
    {
      string candidate = Path.Combine(folder.FullName, "src", "Radio.Web", "wwwroot", "css", "design-system.css");
      if (File.Exists(candidate))
      {
        return candidate;
      }
    }

    throw new FileNotFoundException("design-system.css was not found above the test binaries.");
  }
}
