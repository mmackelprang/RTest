using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Services;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// PHN-11: the incoming-call banner as drawn. Calls are raised through the same internal hub seams the
/// live <c>/hub</c> handlers use; time is the harness's <c>FakeTimeProvider</c>; every async effect is
/// awaited through the service's completion tasks, and bUnit's <c>WaitForAssertion</c> covers only the
/// hop from the service's <c>Changed</c> event onto the renderer.
/// </summary>
public class IncomingCallBannerTests : TestContext
{
  private const string Number = "5550137424";
  private const string Formatted = "(555) 013-7424";

  private IncomingCallBannerHarness _h = null!;

  private IRenderedComponent<IncomingCallBanner> Render(
    bool declineSupported = false, Action<ComponentParameterCollectionBuilder<IncomingCallBanner>>? parameters = null)
  {
    _h = new IncomingCallBannerHarness(declineSupported);
    Services.AddRadzenComponents();
    Services.AddSingleton(_h.Service);
    JSInterop.Mode = JSRuntimeMode.Loose;
    return parameters is null ? RenderComponent<IncomingCallBanner>() : RenderComponent(parameters);
  }

  [Fact]
  public void WithNoCall_OnlyTheEmptyLiveRegionsAreRendered()
  {
    var cut = Render();

    Assert.Empty(cut.FindAll(".icb-scrim"));
    Assert.Equal("", cut.Find("[role=alert]").TextContent);
    Assert.Equal("", cut.Find("[role=status]").TextContent);
  }

  [Fact]
  public async Task AnIncomingCall_ShowsTheBanner_AndAnnouncesTheCallerOnce()
  {
    var cut = Render();

    _h.Hub.RaiseIncomingCallForTest("default", Number);
    await _h.Service.LastNameLookup;

    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));
    Assert.Equal("INCOMING CALL", cut.Find(".icb-header").TextContent);
    Assert.Equal(Formatted, cut.Find(".icb-primary").TextContent);
    Assert.Contains("icb-primary--number", cut.Find(".icb-primary").ClassName);
    Assert.Equal("Touch anywhere to close. The call keeps ringing.", cut.Find(".icb-hint").TextContent);
    Assert.Equal($"Incoming call from {Formatted}", cut.Find("[role=alert]").TextContent);
    Assert.Equal("dialog", cut.Find(".icb-scrim").GetAttribute("role"));
  }

  [Fact]
  public async Task AContact_ShowsTheMonogram_TheName_AndTheNumberUnderIt()
  {
    var cut = Render();
    _h.Phone.PbapName = "Carol Anderson";

    _h.Hub.RaiseIncomingCallForTest("default", Number);
    await _h.Service.LastNameLookup;

    cut.WaitForAssertion(() => Assert.Equal("Carol Anderson", cut.Find(".icb-primary").TextContent));
    Assert.Equal("CA", cut.Find(".icb-monogram").TextContent);
    Assert.Equal(Formatted, cut.Find(".icb-secondary").TextContent);
    Assert.DoesNotContain("icb-primary--number", cut.Find(".icb-primary").ClassName);
  }

  [Fact]
  public void AnUnknownCaller_SaysSo_WithNoCallerIdUnderIt()
  {
    var cut = Render();

    _h.Hub.RaiseIncomingCallForTest("default", "Unknown");

    cut.WaitForAssertion(() => Assert.Equal("Unknown caller", cut.Find(".icb-primary").TextContent));
    Assert.Equal("No caller ID", cut.Find(".icb-secondary").TextContent);
    Assert.Empty(cut.FindAll(".icb-monogram"));
    Assert.Equal("Incoming call from unknown caller", cut.Find("[role=alert]").TextContent);
  }

  [Fact]
  public void ABareRinging_HoldsTheCallerLineBack_UntilTheCallerIdArrives()
  {
    var cut = Render();

    _h.Hub.RaiseCallStateChangedForTest("default", "Ringing");

    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));
    Assert.Empty(cut.FindAll(".icb-primary"));
    Assert.Equal("", cut.Find("[role=alert]").TextContent);
  }

  [Fact]
  public void ATouchOnTheBanner_ClosesIt_AndDeclinesNothing()
  {
    var cut = Render(declineSupported: true);
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    cut.Find(".icb-scrim").Click();
    cut.WaitForAssertion(() => Assert.Contains("icb--leaving", cut.Find(".icb-scrim").ClassName));
    _h.Time.Advance(IncomingCallBannerService.ExitFade);

    cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".icb-scrim")));
    Assert.Equal(0, _h.Phone.Count("/api/phone/decline"));
  }

  [Fact]
  public void ATouchOnTheCard_ClosesItToo()
  {
    var cut = Render();
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    cut.Find(".icb-primary").Click();

    cut.WaitForAssertion(() => Assert.Contains("icb--leaving", cut.Find(".icb-scrim").ClassName));
  }

  [Fact]
  public void ATouchInTheIgnoreColumnOutsideTheButton_DoesNotClose_AndDeclinesNothing()
  {
    var cut = Render(declineSupported: true);
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    // bUnit bubbles a click from the element that handles it up through its ancestors, honouring
    // stopPropagation — so without it on the column this click would reach the scrim and close the banner.
    cut.Find(".icb-ignore-zone").Click();

    Assert.DoesNotContain("icb--leaving", cut.Find(".icb-scrim").ClassName);
    Assert.Equal(IncomingCallPhase.Ringing, _h.Service.Current.Phase);
    Assert.False(_h.Service.Current.IsLeaving);
    Assert.Equal(0, _h.Phone.Count("/api/phone/decline"));
  }

  [Fact]
  public void Escape_ClosesTheBanner()
  {
    var cut = Render();
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    cut.Find(".icb-scrim").KeyDown(new KeyboardEventArgs { Key = "Escape" });

    cut.WaitForAssertion(() => Assert.Contains("icb--leaving", cut.Find(".icb-scrim").ClassName));
  }

  [Fact]
  public void PickingUpTheHandset_ShowsAnswered_ThenTheBannerGoes()
  {
    var cut = Render();
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    _h.Hub.RaiseCallStateChangedForTest("default", "InCall");
    cut.WaitForAssertion(() => Assert.Equal("ANSWERED", cut.Find(".icb-header").TextContent));
    Assert.Contains("icb--answered", cut.Find(".icb-scrim").ClassName);
    Assert.Equal("Call answered", cut.Find("[role=status]").TextContent);

    _h.Time.Advance(IncomingCallBannerService.ExitHold);
    _h.Time.Advance(IncomingCallBannerService.ExitFade);
    cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".icb-scrim")));
  }

  [Fact]
  public void TheCallerHangingUp_ShowsCallEnded_ThenTheBannerGoes()
  {
    var cut = Render();
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    _h.Hub.RaiseCallStateChangedForTest("default", "Idle");
    cut.WaitForAssertion(() => Assert.Equal("CALL ENDED", cut.Find(".icb-header").TextContent));
    Assert.Contains("icb--ended", cut.Find(".icb-scrim").ClassName);

    _h.Time.Advance(IncomingCallBannerService.ExitHold);
    _h.Time.Advance(IncomingCallBannerService.ExitFade);
    cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".icb-scrim")));
  }

  [Fact]
  public void WithDeclineUnsupported_IgnoreIsShownDisabled_WithTheReason()
  {
    var cut = Render(declineSupported: false);
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    var button = cut.Find(".icb-ignore");
    Assert.True(button.HasAttribute("disabled"));
    Assert.Equal("Ignore call, not available yet", button.GetAttribute("aria-label"));
    Assert.Equal("Not available yet. Answer and hang up on the phone, or let it ring.",
      cut.Find(".icb-ignore-sub").TextContent.Trim());
  }

  [Fact]
  public async Task Ignore_CallsTheDeclineEndpointOnce_ThenWaitsForTheCallToEnd()
  {
    var cut = Render(declineSupported: true);
    _h.Hub.RaiseIncomingCallForTest("hall", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));
    Assert.Equal("Ends the call", cut.Find(".icb-ignore-sub").TextContent.Trim());
    Assert.Equal($"Ignore call from {Formatted}", cut.Find(".icb-ignore").GetAttribute("aria-label"));

    await cut.Find(".icb-ignore").ClickAsync(new MouseEventArgs());
    cut.WaitForAssertion(() => Assert.Contains("ENDING CALL", cut.Find(".icb-ignore").TextContent));

    Assert.Equal(1, _h.Phone.Count("/api/phone/decline"));
    Assert.True(cut.Find(".icb-ignore").HasAttribute("disabled"));
    Assert.Contains("icb--declining", cut.Find(".icb-scrim").ClassName);
    Assert.DoesNotContain("icb--leaving", cut.Find(".icb-scrim").ClassName);

    // A touch elsewhere does not close it while the outcome is pending (spec §7).
    cut.Find(".icb-scrim").Click();
    Assert.DoesNotContain("icb--leaving", cut.Find(".icb-scrim").ClassName);

    _h.Hub.RaiseCallStateChangedForTest("hall", "Idle");
    cut.WaitForAssertion(() => Assert.Equal("CALL ENDED", cut.Find(".icb-header").TextContent));
    Assert.Equal(1, _h.Phone.Count("/api/phone/decline"));
  }

  [Fact]
  public async Task AFailedDecline_ShowsTheError_AndLeavesTheBannerUp()
  {
    var cut = Render(declineSupported: true);
    _h.Phone.DeclineResponse = (HttpStatusCode.InternalServerError, "{}", "application/json");
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    await cut.Find(".icb-ignore").ClickAsync(new MouseEventArgs());

    cut.WaitForAssertion(() =>
      Assert.Equal("Couldn't end the call. Try again.", cut.Find(".icb-ignore-error").TextContent));
    Assert.Equal("alert", cut.Find(".icb-ignore-error").GetAttribute("role"));
    Assert.False(cut.Find(".icb-ignore").HasAttribute("disabled"));
    Assert.Equal("INCOMING CALL", cut.Find(".icb-header").TextContent);
    Assert.Equal(1, _h.Phone.Count("/api/phone/decline"));
  }

  [Fact]
  public void TheHost_IsToldWhenTheBannerStartsAndStopsCovering()
  {
    var reports = new List<bool>();
    var cut = Render(parameters: p => p.Add(c => c.OnCoveringChanged, (bool covering) => reports.Add(covering)));

    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Equal(new[] { true }, reports));

    _h.Hub.RaiseCallStateChangedForTest("default", "Idle");
    _h.Time.Advance(IncomingCallBannerService.ExitHold);
    // Still covering through the beat: a panel lit for the call stays lit until the banner is gone.
    Assert.Equal(new[] { true }, reports);
    _h.Time.Advance(IncomingCallBannerService.ExitFade);

    cut.WaitForAssertion(() => Assert.Equal(new[] { true, false }, reports));
  }

  [Fact]
  public void TheLayoutHost_LiftsTheIdleDim_WhenACallArrives()
  {
    var cut = Render(parameters: p => p.Add(c => c.UndimOnShow, true));

    _h.Hub.RaiseIncomingCallForTest("default", Number);

    cut.WaitForAssertion(() => Assert.Single(
      JSInterop.Invocations, i => i.Identifier == "radioSleepManager.wake" && Equals(i.Arguments[0], "call")));
  }

  [Fact]
  public void ASecondHostForTheSameCall_NeitherReannouncesNorReplaysTheEntry()
  {
    // The /sleep → layout move mid-ring: one circuit, one service, two component instances.
    var first = Render();
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    first.WaitForAssertion(() => Assert.Single(first.FindAll(".icb-scrim")));
    Assert.DoesNotContain("icb--no-entry", first.Find(".icb-scrim").ClassName);

    var second = RenderComponent<IncomingCallBanner>();

    Assert.Contains("icb--no-entry", second.Find(".icb-scrim").ClassName);
    Assert.Equal("", second.Find("[role=alert]").TextContent);
    // Not politely either: the host that took over says nothing until something changes.
    Assert.Equal("", second.Find("[role=status]").TextContent);
  }

  [Fact]
  public void TheSameCallerCallingBack_IsAnnouncedAgain()
  {
    var cut = Render();
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Equal($"Incoming call from {Formatted}", cut.Find("[role=alert]").TextContent));

    _h.Hub.RaiseCallStateChangedForTest("default", "Idle");
    _h.Time.Advance(IncomingCallBannerService.ExitHold);
    _h.Time.Advance(IncomingCallBannerService.ExitFade);
    cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".icb-scrim")));
    var between = cut.Find("[role=alert]").TextContent;

    _h.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    _h.Hub.RaiseIncomingCallForTest("default", Number);

    // Emptied in between, so the same words are a change a screen reader announces.
    Assert.Equal("", between);
    cut.WaitForAssertion(() => Assert.Equal($"Incoming call from {Formatted}", cut.Find("[role=alert]").TextContent));
  }

  [Fact]
  public async Task ACallAlreadyUpWhenTheBannerMounts_IsAnnouncedAfterTheFirstRender()
  {
    // A reload mid-ring. The region must exist empty first (spec §11), then get the text.
    _h = new IncomingCallBannerHarness();
    _h.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\"}}";
    _h.Start();
    await _h.Service.Seeded;
    Services.AddRadzenComponents();
    Services.AddSingleton(_h.Service);
    JSInterop.Mode = JSRuntimeMode.Loose;

    var cut = RenderComponent<IncomingCallBanner>();

    cut.WaitForAssertion(() => Assert.Equal($"Incoming call from {Formatted}", cut.Find("[role=alert]").TextContent));
    Assert.Single(cut.FindAll(".icb-scrim"));
  }

  [Fact]
  public async Task WhileDeclining_IgnoresSpokenNameMatchesWhatItShows()
  {
    var cut = Render(declineSupported: true);
    _h.Hub.RaiseIncomingCallForTest("default", Number);
    cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".icb-scrim")));

    await cut.Find(".icb-ignore").ClickAsync(new MouseEventArgs());

    cut.WaitForAssertion(() => Assert.Equal("Ending call", cut.Find(".icb-ignore").GetAttribute("aria-label")));
  }
}
