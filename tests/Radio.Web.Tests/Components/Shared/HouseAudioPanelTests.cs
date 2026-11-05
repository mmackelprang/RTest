using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radio.Web.Components.Shared.HouseAudio;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// Against the real poller and client, on the fake controller. Nothing waits on elapsed time
/// (<c>CLAUDE.md</c> § Test Timing): the clock is fake, each wait is on a request or on rendered
/// markup, and <see cref="Patience"/> is only the deadlock guard.
/// </summary>
public class HouseAudioPanelTests : TestContext
{
  private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
  private static readonly TimeSpan Patience = RecordingHandler.RendezvousTimeout;

  private readonly FakeTimeProvider _time = new();
  private readonly FakeHouseController _controller = new();
  private HouseAudioPasswordStore _passwords = FakeHouseController.StoreWithPassword();

  public HouseAudioPanelTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private IRenderedComponent<HouseAudioPanel> Render()
  {
    HouseAudioApiService api = _controller.CreateClient(_passwords);
    Services.AddSingleton(api);
    Services.AddSingleton<Func<HouseAudioPoller>>(() => new HouseAudioPoller(api, _time, Interval));
    return RenderComponent<HouseAudioPanel>();
  }

  private static bool IsSystemPoll(HttpMethod method, string path) => method == HttpMethod.Get && path == "/system";

  private static bool IsWrite(HttpMethod method, string path) => method != HttpMethod.Get;

  private Task PollsBegunAsync(int count) => _controller.Handler.WaitForAsync(IsSystemPoll, count);

  /// <summary>Lets one more poll run. Requires that the previous one has already been raised.</summary>
  private async Task NextPollAsync()
  {
    int begun = _controller.Handler.Count(IsSystemPoll);
    _time.Advance(Interval);
    await PollsBegunAsync(begun + 1);
  }

  private static void WaitForMarkup(IRenderedComponent<HouseAudioPanel> cut, string text) =>
    cut.WaitForAssertion(() => cut.Markup.Should().Contain(text), Patience);

  private static void WaitForLevel(IRenderedComponent<HouseAudioPanel> cut, string zone, string level) =>
    cut.WaitForAssertion(() => Level(cut, zone).Should().Be(level), Patience);

  private static AngleSharp.Dom.IElement Card(IRenderedComponent<HouseAudioPanel> cut, string zone) =>
    cut.FindAll("[data-testid='house-zone-card']").Single(card => card.GetAttribute("aria-label") == zone);

  private static string Level(IRenderedComponent<HouseAudioPanel> cut, string zone) =>
    Card(cut, zone).QuerySelector("[data-testid='house-zone-level']")!.TextContent.Trim();

  private static void Tap(IRenderedComponent<HouseAudioPanel> cut, string zone, string control) =>
    Card(cut, zone).QuerySelector($"[data-testid='{control}']")!.Click();

  /// <summary>A finger sets the volume: the same path the slider's own script takes.</summary>
  private static Task SetVolumeAsync(IRenderedComponent<HouseAudioPanel> cut, string zone, double fraction)
  {
    Radio.Web.Components.Shared.SeekBar bar = cut.FindComponents<Radio.Web.Components.Shared.SeekBar>()
      .Single(component => component.Find(".seek-bar").GetAttribute("aria-label")!.StartsWith(zone, StringComparison.Ordinal))
      .Instance;

    return cut.InvokeAsync(async () =>
    {
      await bar.OnDragMove(fraction);
      await bar.OnDragEnd(fraction);
    });
  }

  [Fact]
  public void Panel_ShowsTheControllerName()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();

    WaitForMarkup(cut, "Mackelhome Controller");
  }

  [Fact]
  public async Task Panel_MuteAll_And_AllOff_CallTheApi()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");

    cut.Find("[data-testid='house-mute-all']").Click();
    cut.Find("[data-testid='house-all-off']").Click();
    await _controller.Handler.WaitForAsync(IsWrite, 2);

    _controller.Seen.Where(seen => seen.Method == HttpMethod.Put).Select(seen => seen.Uri.AbsolutePath)
      .Should().Equal("/mute", "/off");
  }

  [Fact]
  public async Task Panel_MuteAll_WhenEveryPoweredRoomIsMuted_OffersUnmute()
  {
    _controller.SetMuted("Kitchen", true);
    _controller.SetPower("Den", false);
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Unmute all");

    cut.Find("[data-testid='house-mute-all']").Click();
    await _controller.Handler.WaitForAsync(IsWrite, 1);

    _controller.Seen.Single(seen => seen.Method == HttpMethod.Put).Uri.AbsolutePath.Should().Be("/unmute");
  }

  [Fact]
  public async Task Panel_CardControls_CallTheApi_ForThatRoom()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");

    Tap(cut, "Den", "house-zone-power");
    await _controller.Handler.WaitForAsync(IsWrite, 1);
    Tap(cut, "Kitchen", "house-zone-mute");
    await _controller.Handler.WaitForAsync(IsWrite, 2);
    await SetVolumeAsync(cut, "Kitchen", 0.5);
    await _controller.Handler.WaitForAsync(IsWrite, 3);

    Tap(cut, "Kitchen", "house-zone-source");
    Card(cut, "Kitchen").QuerySelectorAll("[data-testid='house-source-option']")[0].Click();
    await _controller.Handler.WaitForAsync(IsWrite, 4);

    _controller.Seen.Where(seen => seen.Method == HttpMethod.Put).Select(seen => seen.Uri.AbsolutePath)
      .Should().Equal("/Den/off", "/Kitchen/mute", "/Kitchen/volume/50", "/Kitchen/source/0");
  }

  [Fact]
  public void Panel_Unreachable_ShowsTheMessage_AndKeepsRetrying()
  {
    _controller.Failure = new HttpRequestException("connection refused");
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "reach the controller");
    cut.FindAll("[data-testid='house-audio-empty']").Should().BeEmpty("the status line already says what is wrong");

    _controller.Failure = null;
    _time.Advance(Interval);

    WaitForMarkup(cut, "Mackelhome Controller");
    cut.Markup.Should().NotContain("reach the controller");
  }

  [Fact]
  public void Panel_AmpDisconnected_NamesTheAmp()
  {
    _controller.AmpLinkDown = true;

    IRenderedComponent<HouseAudioPanel> cut = Render();

    WaitForMarkup(cut, "reach the amp");
  }

  [Fact]
  public void Panel_WrongPassword_PointsToControllerSettings()
  {
    _controller.ForcedStatus = HttpStatusCode.Unauthorized;

    IRenderedComponent<HouseAudioPanel> cut = Render();

    WaitForMarkup(cut, "refused the password");
    cut.Find("[data-testid='house-audio-status']").TextContent.Should().Contain("Controller settings");
    cut.Find("[data-testid='house-controller-settings']").TextContent.Should().Contain("Controller settings");
  }

  [Fact]
  public void Panel_NoPassword_PointsToControllerSettings_AndSendsNothing()
  {
    _passwords = FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, new RecordingHandler());

    IRenderedComponent<HouseAudioPanel> cut = Render();

    WaitForMarkup(cut, "No controller password");
    cut.Find("[data-testid='house-audio-status']").TextContent.Should().Contain("Controller settings");
    _controller.Seen.Should().BeEmpty();
  }

  [Fact]
  public async Task Panel_Stale_MarksLastKnownRooms()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");
    cut.FindAll("[data-testid='house-zone-stale']").Should().BeEmpty();

    _controller.Failure = new HttpRequestException("connection refused");
    await NextPollAsync();

    cut.WaitForAssertion(() => cut.FindAll("[data-testid='house-zone-stale']").Should().HaveCount(2), Patience);
    cut.Markup.Should().Contain("reach the controller").And.Contain("Kitchen");
  }

  [Fact]
  public async Task Panel_WhenDisposed_StopsPolling()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");
    _controller.SystemGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await NextPollAsync();

    DisposeComponents();

    // Only the poller's disposal can cancel that request, so this cannot pass by being quick.
    await _controller.RequestCancelled.Task.WaitAsync(Patience);
  }

  [Fact]
  public async Task Panel_MutedRoom_ReportingVolumeZero_StillShowsThatItIsMuted()
  {
    _controller.SetVolume("Kitchen", 50);
    _controller.SetMuted("Kitchen", true);

    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForLevel(cut, "Kitchen", "0");

    Card(cut, "Kitchen").QuerySelector("[data-testid='house-zone-mute']")!.GetAttribute("aria-pressed").Should().Be("true");
    Card(cut, "Kitchen").QuerySelector("[data-testid='house-zone-source']")!.TextContent.Should().Contain("muted");

    // Unmuting shows at once, and the room's own volume comes back with the next report.
    Tap(cut, "Kitchen", "house-zone-mute");
    await _controller.Handler.WaitForAsync(IsWrite, 1);
    _controller.SetMuted("Kitchen", false);
    await NextPollAsync();

    WaitForLevel(cut, "Kitchen", "50");
    Card(cut, "Kitchen").QuerySelector("[data-testid='house-zone-mute']")!.GetAttribute("aria-pressed").Should().Be("false");
  }

  [Fact]
  public async Task Panel_FailedCommand_ReturnsToReportedState_AndShowsIt()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");
    _controller.WriteStatus = HttpStatusCode.Conflict;

    await SetVolumeAsync(cut, "Kitchen", 0.6);

    WaitForLevel(cut, "Kitchen", "20");
    cut.Find("[data-testid='house-audio-command-error']").TextContent.Should().Contain("Kitchen").And.Contain("refused");
  }

  [Fact]
  public async Task Panel_FailedCommand_ClearsAtTheNextHealthySnapshot()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");
    _controller.WriteStatus = HttpStatusCode.Conflict;
    await SetVolumeAsync(cut, "Kitchen", 0.6);
    cut.WaitForAssertion(() => cut.FindAll("[data-testid='house-audio-command-error']").Should().ContainSingle(), Patience);

    await NextPollAsync();

    cut.WaitForAssertion(() => cut.FindAll("[data-testid='house-audio-command-error']").Should().BeEmpty(), Patience);
  }

  [Fact]
  public async Task Panel_FailedCommand_ThenAHealthChange_ShowsBoth()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");
    _controller.WriteStatus = HttpStatusCode.Conflict;
    await SetVolumeAsync(cut, "Kitchen", 0.6);
    cut.WaitForAssertion(() => cut.FindAll("[data-testid='house-audio-command-error']").Should().ContainSingle(), Patience);

    _controller.AmpLinkDown = true;
    await NextPollAsync();

    cut.WaitForAssertion(
      () => cut.Find("[data-testid='house-audio-status']").TextContent.Should().Contain("reach the amp"), Patience);
    cut.Find("[data-testid='house-audio-command-error']").TextContent.Should().Contain("Kitchen",
      "a stale snapshot says nothing about the command, so the failure stays");
  }

  [Fact]
  public async Task Panel_NextCommand_ClearsAFailure()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");
    _controller.WriteStatus = HttpStatusCode.Conflict;
    await SetVolumeAsync(cut, "Kitchen", 0.6);
    cut.WaitForAssertion(() => cut.FindAll("[data-testid='house-audio-command-error']").Should().ContainSingle(), Patience);

    _controller.WriteStatus = null;
    await SetVolumeAsync(cut, "Kitchen", 0.3);

    cut.WaitForAssertion(() => cut.FindAll("[data-testid='house-audio-command-error']").Should().BeEmpty(), Patience);
  }

  [Fact]
  public async Task Panel_HungCommand_KeepsTheCommandedValue_ThroughAHeldSnapshot()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");
    _controller.WriteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    Task commanded = SetVolumeAsync(cut, "Kitchen", 0.6);
    await _controller.Handler.WaitForAsync(IsWrite, 1);
    WaitForLevel(cut, "Kitchen", "60");

    // Poll 3 beginning means poll 2 has been raised, with the volume from before the command.
    await NextPollAsync();
    await NextPollAsync();
    await cut.InvokeAsync(() => { });
    Level(cut, "Kitchen").Should().Be("60", "a held snapshot must not snap the card back");

    // The command lands, and the room settles somewhere else. The first report after it wins.
    _controller.SetVolume("Kitchen", 64);
    _controller.WriteGate.SetResult();
    await commanded;
    await NextPollAsync();

    WaitForLevel(cut, "Kitchen", "64");
  }

  [Fact]
  public async Task Panel_AllOff_ThatFails_PutsEveryRoomBack()
  {
    IRenderedComponent<HouseAudioPanel> cut = Render();
    WaitForMarkup(cut, "Kitchen");
    _controller.WriteStatus = HttpStatusCode.ServiceUnavailable;

    cut.Find("[data-testid='house-all-off']").Click();

    cut.WaitForAssertion(() => cut.Find("[data-testid='house-audio-command-error']").TextContent.Should().Contain("amp"), Patience);
    cut.FindAll("[data-testid='house-zone-power']").Should()
      .OnlyContain(button => button.GetAttribute("aria-pressed") == "true");
  }
}
