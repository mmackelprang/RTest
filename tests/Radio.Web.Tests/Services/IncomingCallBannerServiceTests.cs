using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Utilities;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Radio.Web.Tests.TestHelpers;
using Rig = Radio.Web.Tests.TestHelpers.IncomingCallBannerHarness;

namespace Radio.Web.Tests.Services;

/// <summary>
/// PHN-11: the incoming-call banner's state machine, driven through the same internal hub seams the live
/// <c>/hub</c> handlers are wired to.
/// </summary>
/// <remarks>
/// No wall clock anywhere (CLAUDE.md § Test Timing). Time is a <see cref="FakeTimeProvider"/>; every async
/// effect is awaited through the service's own completion tasks (<c>LastPoll</c>, <c>LastNameLookup</c>,
/// <c>Seeded</c>) or through the <see cref="IncomingCallBannerService.DeclineAsync"/> task itself, so each
/// assertion runs after the thing it asserts on has finished, not after a guess at how long it takes.
/// </remarks>
public class IncomingCallBannerServiceTests
{
  private const string Number = "5550137424";
  private const string Formatted = "(555) 013-7424";

  // ── showing ───────────────────────────────────────────────────────

  [Fact]
  public async Task IncomingCall_ShowsTheBanner_WithTheNumber_AndThenTheContactName()
  {
    var rig = new Rig();
    rig.Phone.PbapName = "Grandma Anderson";
    // Hold the lookup open: without the gate it can finish on another thread before the first read,
    // and whether the test saw the in-between state would depend on the scheduler.
    var gate = new TaskCompletionSource();
    rig.Phone.PbapGate = gate.Task;
    rig.Start();

    rig.Hub.RaiseIncomingCallForTest("default", Number);
    var first = rig.Service.Current;
    gate.SetResult();
    await rig.Service.LastNameLookup;
    var resolved = rig.Service.Current;

    Assert.True(first.IsVisible);
    Assert.Equal(Formatted, first.PrimaryText);
    Assert.True(first.IsResolvingName);

    Assert.Equal(IncomingCallCallerKind.Contact, resolved.CallerKind);
    Assert.Equal("Grandma Anderson", resolved.PrimaryText);
    Assert.Equal(Formatted, resolved.SecondaryText);
    Assert.Equal("GA", resolved.Monogram);
    Assert.False(resolved.IsResolvingName);
  }

  [Fact]
  public async Task NoContactAnywhere_ShowsTheFormattedNumberAlone()
  {
    var rig = new Rig();
    rig.Start();

    rig.Hub.RaiseIncomingCallForTest("default", Number);
    await rig.Service.LastNameLookup;
    var s = rig.Service.Current;

    Assert.Equal(IncomingCallCallerKind.Number, s.CallerKind);
    Assert.Equal(Formatted, s.PrimaryText);
    Assert.Null(s.SecondaryText);
    Assert.Null(s.Monogram);
  }

  [Fact]
  public async Task APbapMiss_FallsBackToRotaryPhonesOwnContacts()
  {
    var rig = new Rig();
    rig.Phone.ContactsJson = $"[{{\"id\":\"1\",\"name\":\"Uncle Bob\",\"phoneNumber\":\"+1 (555) 013-7424\"}}]";
    rig.Start();

    rig.Hub.RaiseIncomingCallForTest("default", Number);
    await rig.Service.LastNameLookup;

    Assert.Equal("Uncle Bob", rig.Service.Current.PrimaryText);
    Assert.Equal(IncomingCallCallerKind.Contact, rig.Service.Current.CallerKind);
  }

  [Theory]
  [InlineData("Unknown")]
  [InlineData("")]
  public void ANumberRotaryPhoneDoesNotHave_ShowsUnknownCaller(string number)
  {
    var rig = new Rig();
    rig.Start();

    rig.Hub.RaiseIncomingCallForTest("default", number);
    var s = rig.Service.Current;

    Assert.True(s.IsVisible);
    Assert.Equal(IncomingCallCallerKind.Unknown, s.CallerKind);
    Assert.Equal("Unknown caller", s.PrimaryText);
    Assert.False(s.IsAwaitingCallerId);
    Assert.Equal(0, rig.Phone.Count("/api/bluetooth/pbap/lookup"));
  }

  [Fact]
  public async Task ABareRinging_IsAwaitingTheCallerId_UntilTheIncomingCallThatFollowsIt()
  {
    var rig = new Rig();
    rig.Start();

    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    var bare = rig.Service.Current;
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    await rig.Service.LastNameLookup;

    Assert.True(bare.IsVisible);
    Assert.True(bare.IsAwaitingCallerId);
    Assert.False(rig.Service.Current.IsAwaitingCallerId);
    Assert.Equal(Formatted, rig.Service.Current.PrimaryText);
  }

  [Fact]
  public async Task ABareRingingThatNoNumberFollows_SettlesAsUnknownCaller_OnTheFirstStatusRead()
  {
    var rig = new Rig();
    rig.Start();

    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    rig.Phone.StatusJson = "{\"callState\":\"Ringing\",\"incomingNumber\":null}";
    await rig.Tick();

    var s = rig.Service.Current;
    Assert.True(s.IsVisible);
    Assert.False(s.IsAwaitingCallerId);
    Assert.Equal("Unknown caller", s.PrimaryText);
  }

  [Fact]
  public async Task ACircuitThatOpensMidRing_ShowsTheCallFromTheStatusRead()
  {
    var rig = new Rig();
    rig.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\"}}";
    rig.Start();
    await rig.Service.Seeded;
    await rig.Service.LastNameLookup;

    Assert.True(rig.Service.Current.IsVisible);
    Assert.Equal(Formatted, rig.Service.Current.PrimaryText);
  }

  // ── closing ───────────────────────────────────────────────────────

  [Fact]
  public void ATouch_ClosesTheBannerAtOnce_WithNoBeat_AndDeclinesNothing()
  {
    var rig = new Rig(declineSupported: true);
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);

    rig.Service.Dismiss();
    var fading = rig.Service.Current;
    rig.Time.Advance(IncomingCallBannerService.ExitFade);

    Assert.True(fading.IsLeaving);
    Assert.Equal(IncomingCallPhase.Ringing, fading.Phase);
    Assert.False(rig.Service.Current.IsVisible);
    Assert.Equal(IncomingCallCloseReason.Touch, rig.Service.Current.LastCloseReason);
    Assert.Equal(0, rig.Phone.Count("/api/phone/decline"));
    Assert.Equal(0, rig.Phone.Count("/api/phone/simulate"));
  }

  [Fact]
  public void ATouchClosedBanner_StaysClosedThroughACallerIdUpdate_AndTheNextCallShowsAgain()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    rig.Hub.RaiseIncomingCallForTest("default", "Unknown");
    rig.Service.Dismiss();
    rig.Time.Advance(IncomingCallBannerService.ExitFade);

    // RotaryPhone re-broadcasts Ringing + IncomingCall when +CLIP arrives after +CIEV: the same call.
    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    var afterUpdate = rig.Service.Current;

    rig.Hub.RaiseCallStateChangedForTest("default", "Idle");
    var afterEnd = rig.Service.Current;
    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    rig.Hub.RaiseIncomingCallForTest("default", "5550100000");

    Assert.False(afterUpdate.IsVisible);
    Assert.False(afterEnd.IsVisible);   // a touch-closed call ending plays no beat
    Assert.True(rig.Service.Current.IsVisible);
    Assert.Equal("(555) 010-0000", rig.Service.Current.PrimaryText);
  }

  [Fact]
  public void PickingUpTheHandset_ShowsAnswered_ThenFades_ThenCloses()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    var callId = rig.Service.Current.CallId;

    rig.Hub.RaiseCallStateChangedForTest("default", "InCall");
    var beat = rig.Service.Current;
    rig.Time.Advance(IncomingCallBannerService.ExitHold - TimeSpan.FromMilliseconds(1));
    var stillHolding = rig.Service.Current;
    rig.Time.Advance(TimeSpan.FromMilliseconds(1));
    var fading = rig.Service.Current;
    rig.Time.Advance(IncomingCallBannerService.ExitFade);

    Assert.True(beat.IsVisible);
    Assert.Equal(IncomingCallPhase.Answered, beat.Phase);
    Assert.Equal(callId, beat.CallId);
    Assert.False(stillHolding.IsLeaving);
    Assert.True(fading.IsLeaving);
    Assert.False(rig.Service.Current.IsVisible);
    Assert.Equal(IncomingCallCloseReason.Answered, rig.Service.Current.LastCloseReason);
  }

  [Fact]
  public void TheCallerHangingUp_ShowsCallEnded_ThenCloses()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);

    rig.Hub.RaiseCallStateChangedForTest("default", "Idle");
    var beat = rig.Service.Current;
    // Two steps: the fade timer is armed when the hold ends, so it is due ExitFade after THAT moment.
    rig.Time.Advance(IncomingCallBannerService.ExitHold);
    rig.Time.Advance(IncomingCallBannerService.ExitFade);

    Assert.Equal(IncomingCallPhase.Ended, beat.Phase);
    Assert.False(rig.Service.Current.IsVisible);
    Assert.Equal(IncomingCallCloseReason.Ended, rig.Service.Current.LastCloseReason);
  }

  [Fact]
  public void ATouchDuringTheBeat_SkipsTheRestOfIt()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    rig.Hub.RaiseCallStateChangedForTest("default", "InCall");

    rig.Service.Dismiss();
    var fading = rig.Service.Current;
    rig.Time.Advance(IncomingCallBannerService.ExitFade);

    Assert.True(fading.IsLeaving);
    Assert.False(rig.Service.Current.IsVisible);
  }

  [Fact]
  public void ANewRingDuringTheBeat_CancelsIt_AndShowsAFreshBanner()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    var firstId = rig.Service.Current.CallId;
    rig.Hub.RaiseCallStateChangedForTest("default", "Idle");

    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    rig.Hub.RaiseIncomingCallForTest("default", "5550100000");
    rig.Time.Advance(IncomingCallBannerService.ExitHold);
    rig.Time.Advance(IncomingCallBannerService.ExitFade);

    var s = rig.Service.Current;
    Assert.True(s.IsVisible);
    Assert.Equal(IncomingCallPhase.Ringing, s.Phase);
    Assert.NotEqual(firstId, s.CallId);
    Assert.False(s.IsLeaving);
  }

  [Fact]
  public void AnotherPhonesStateChange_DoesNotCloseTheBanner()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("hall", Number);

    rig.Hub.RaiseCallStateChangedForTest("kitchen", "Idle");

    Assert.True(rig.Service.Current.IsVisible);
    Assert.Equal(IncomingCallPhase.Ringing, rig.Service.Current.Phase);
  }

  [Fact]
  public async Task AMissedHangUp_IsCaughtByTheStatusRead()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    rig.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\"}}";
    await rig.Tick();
    var stillRinging = rig.Service.Current.Phase;

    rig.Phone.StatusJson = "{\"callState\":\"Idle\"}";
    await rig.Tick();

    Assert.Equal(IncomingCallPhase.Ringing, stillRinging);
    Assert.Equal(IncomingCallPhase.Ended, rig.Service.Current.Phase);
    Assert.Equal(IncomingCallCloseReason.Ended, rig.Service.Current.LastCloseReason);
  }

  [Fact]
  public async Task AMissedAnswer_IsCaughtByTheStatusRead_AsAnswered()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);

    rig.Phone.StatusJson = "{\"callState\":\"InCall\"}";
    await rig.Tick();

    Assert.Equal(IncomingCallPhase.Answered, rig.Service.Current.Phase);
    Assert.Equal(IncomingCallCloseReason.Answered, rig.Service.Current.LastCloseReason);
  }

  [Fact]
  public async Task WithNothingConfirmingTheRing_TheBannerClosesAfterTheStaleWindow_AndNotBefore()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    rig.Phone.StatusFails = true;

    int ticksToStale = (int)(IncomingCallBannerService.StaleAfter / IncomingCallBannerService.PollInterval);
    for (int i = 1; i < ticksToStale; i++)
    {
      await rig.Tick();
    }
    var justBefore = rig.Service.Current.Phase;
    await rig.Tick();

    Assert.Equal(IncomingCallPhase.Ringing, justBefore);
    Assert.Equal(IncomingCallPhase.Ended, rig.Service.Current.Phase);
    Assert.Equal(IncomingCallCloseReason.Stale, rig.Service.Current.LastCloseReason);
  }

  [Fact]
  public async Task AStatusReadSayingRinging_KeepsTheBannerUpPastTheStaleWindow()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    rig.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\"}}";

    int ticks = (int)(IncomingCallBannerService.StaleAfter / IncomingCallBannerService.PollInterval) + 5;
    for (int i = 0; i < ticks; i++)
    {
      await rig.Tick();
    }

    Assert.True(rig.Service.Current.IsVisible);
    Assert.Equal(IncomingCallPhase.Ringing, rig.Service.Current.Phase);
  }

  [Fact]
  public async Task TheStatusRead_AsksAboutTheRingingPhone()
  {
    var rig = new Rig();
    rig.Start();
    await rig.Service.Seeded;
    rig.Hub.RaiseIncomingCallForTest("hall", Number);
    rig.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\"}}";

    await rig.Tick();

    Assert.Contains(rig.Phone.Requests, r => r.StartsWith("GET /api/phone/status?phoneId=hall", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AHungStatusRead_IsNotStackedOn_TheNextTickSkips()
  {
    var rig = new Rig();
    rig.Start();
    await rig.Service.Seeded;
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    var gate = new TaskCompletionSource();
    rig.Phone.StatusGate = gate.Task;
    int before = rig.Phone.Count("/api/phone/status");

    rig.Time.Advance(IncomingCallBannerService.PollInterval);   // starts a read that hangs
    var hung = rig.Service.LastPoll;
    rig.Time.Advance(IncomingCallBannerService.PollInterval);   // must not start another
    rig.Time.Advance(IncomingCallBannerService.PollInterval);
    int during = rig.Phone.Count("/api/phone/status") - before;
    gate.SetResult();
    await hung;

    Assert.Equal(1, during);
  }

  [Fact]
  public async Task ANewCallIdWhileRinging_SplitsOffTheNewCall_EvenWhenTheOldOneWasTouchClosed()
  {
    // The Idle between two calls on one phone was lost: the hub's next ring merged into the old,
    // touch-closed call, which would keep the new call off the screen. RotaryPhone's CallId says otherwise.
    var rig = new Rig();
    rig.Start();
    await rig.Service.Seeded;
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    rig.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\",\"callId\":\"call-A\"}}";
    await rig.Tick();
    var first = rig.Service.Current.CallId;
    rig.Service.Dismiss();
    rig.Time.Advance(IncomingCallBannerService.ExitFade);

    rig.Hub.RaiseIncomingCallForTest("default", "5550100000");   // merges into the hidden call
    var merged = rig.Service.Current.IsVisible;
    rig.Phone.StatusJson = "{\"callState\":\"Ringing\",\"incomingNumber\":\"5550100000\",\"callId\":\"call-B\"}";
    await rig.Tick();

    Assert.False(merged);
    var s = rig.Service.Current;
    Assert.True(s.IsVisible);
    Assert.NotEqual(first, s.CallId);
    Assert.Equal("(555) 010-0000", s.PrimaryText);
    Assert.Equal(IncomingCallPhase.Ringing, s.Phase);
  }

  [Fact]
  public async Task TheSameCallId_IsTheSameCall()
  {
    var rig = new Rig();
    rig.Start();
    await rig.Service.Seeded;
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    rig.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\",\"callId\":\"call-A\"}}";
    await rig.Tick();
    var first = rig.Service.Current.CallId;

    await rig.Tick();

    Assert.Equal(first, rig.Service.Current.CallId);
  }

  // ── second call ───────────────────────────────────────────────────

  [Fact]
  public async Task ACallerIdUpdateForTheSameCall_UpdatesTheBannerInPlace()
  {
    var rig = new Rig();
    rig.Phone.PbapName = "Grandma Anderson";
    rig.Start();

    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    rig.Hub.RaiseIncomingCallForTest("default", "Unknown");
    var before = rig.Service.Current;
    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    await rig.Service.LastNameLookup;

    Assert.Equal("Unknown caller", before.PrimaryText);
    Assert.Equal(before.CallId, rig.Service.Current.CallId);
    Assert.Equal("Grandma Anderson", rig.Service.Current.PrimaryText);
  }

  [Fact]
  public async Task ALaterUnknown_NeverReplacesAKnownNumber()
  {
    var rig = new Rig();
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    await rig.Service.LastNameLookup;

    rig.Hub.RaiseIncomingCallForTest("default", "Unknown");

    Assert.Equal(Formatted, rig.Service.Current.PrimaryText);
  }

  [Fact]
  public async Task ARingOnAnotherPhone_ShowsTheNewest_AndFallsBackWhenItEnds()
  {
    var rig = new Rig();
    var lookupGate = new TaskCompletionSource();
    rig.Phone.PbapGate = lookupGate.Task;
    rig.Phone.PbapName = "Grandma Anderson";
    rig.Start();

    rig.Hub.RaiseIncomingCallForTest("hall", Number);
    var hallLookup = rig.Service.LastNameLookup;
    rig.Phone.PbapGate = null;
    rig.Phone.PbapName = null;
    rig.Hub.RaiseIncomingCallForTest("kitchen", "5550100000");
    await rig.Service.LastNameLookup;
    var newest = rig.Service.Current;

    // The hall's name arrives late: it belongs to the hall's call, not to the one on screen.
    lookupGate.SetResult();
    await hallLookup;
    var stillKitchen = rig.Service.Current;

    rig.Hub.RaiseCallStateChangedForTest("kitchen", "Idle");
    var fallback = rig.Service.Current;

    Assert.Equal("(555) 010-0000", newest.PrimaryText);
    Assert.Equal("(555) 010-0000", stillKitchen.PrimaryText);
    Assert.Equal(IncomingCallPhase.Ringing, fallback.Phase);
    Assert.Equal("Grandma Anderson", fallback.PrimaryText);
    Assert.False(fallback.IsLeaving);
  }

  [Fact]
  public async Task ANameThatArrivesAfterTheCallEnded_IsDropped()
  {
    var rig = new Rig();
    var gate = new TaskCompletionSource();
    rig.Phone.PbapGate = gate.Task;
    rig.Phone.PbapName = "Grandma Anderson";
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    var lookup = rig.Service.LastNameLookup;

    rig.Hub.RaiseCallStateChangedForTest("default", "Idle");
    gate.SetResult();
    await lookup;

    Assert.Equal(IncomingCallPhase.Ended, rig.Service.Current.Phase);
    Assert.Equal(Formatted, rig.Service.Current.PrimaryText);
  }

  // ── Ignore ────────────────────────────────────────────────────────

  [Fact]
  public async Task Ignore_WhenDeclineIsNotSupported_SendsNothing()
  {
    var rig = new Rig(declineSupported: false);
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);

    await rig.Service.DeclineAsync();

    Assert.False(rig.Service.Current.CanDecline);
    Assert.Equal(0, rig.Phone.Count("/api/phone/decline"));
    Assert.Equal(IncomingCallDeclineState.None, rig.Service.Current.DeclineState);
    Assert.True(rig.Service.Current.IsVisible);
  }

  [Fact]
  public async Task Ignore_DeclinesOnce_ThenTheBannerWaitsForTheCallToEnd()
  {
    var rig = new Rig(declineSupported: true);
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("hall", Number);

    await rig.Service.DeclineAsync();
    await rig.Service.DeclineAsync();   // a second tap while declining sends nothing
    var declining = rig.Service.Current;
    rig.Hub.RaiseCallStateChangedForTest("hall", "Idle");

    Assert.Equal(1, rig.Phone.Count("/api/phone/decline"));
    Assert.Contains("phoneId=hall", rig.Phone.Requests.Single(r => r.Contains("/api/phone/decline")));
    Assert.Equal(IncomingCallPhase.Ringing, declining.Phase);
    Assert.Equal(IncomingCallDeclineState.Declining, declining.DeclineState);
    Assert.Equal(IncomingCallPhase.Ended, rig.Service.Current.Phase);
    Assert.Equal(IncomingCallCloseReason.Ended, rig.Service.Current.LastCloseReason);
  }

  [Fact]
  public async Task Ignore_WhileTheRequestIsInFlight_DoesNotSendASecond()
  {
    var rig = new Rig(declineSupported: true);
    var gate = new TaskCompletionSource();
    rig.Phone.DeclineGate = gate.Task;
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);

    var first = rig.Service.DeclineAsync();
    var inFlight = rig.Service.Current.DeclineState;
    await rig.Service.DeclineAsync();
    gate.SetResult();
    await first;

    Assert.Equal(IncomingCallDeclineState.Declining, inFlight);
    Assert.Equal(1, rig.Phone.Count("/api/phone/decline"));
  }

  [Fact]
  public async Task WhileDeclining_ATouchDoesNotCloseTheBanner()
  {
    var rig = new Rig(declineSupported: true);
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    await rig.Service.DeclineAsync();

    rig.Service.Dismiss();

    Assert.True(rig.Service.Current.IsVisible);
    Assert.False(rig.Service.Current.IsLeaving);
    Assert.Equal(IncomingCallDeclineState.Declining, rig.Service.Current.DeclineState);
  }

  [Fact]
  public async Task AnAcceptedDecline_ThatDoesNotEndTheCallWithinTheDeadline_ReportsAFailure()
  {
    var rig = new Rig(declineSupported: true);
    // The status read that runs inside the deadline must see the call still ringing — the case under test.
    rig.Phone.StatusJson = $"{{\"callState\":\"Ringing\",\"incomingNumber\":\"{Number}\"}}";
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    await rig.Service.DeclineAsync();

    rig.Time.Advance(IncomingCallBannerService.DeclineDeadline - TimeSpan.FromMilliseconds(1));
    await rig.Service.LastPoll;
    var justBefore = rig.Service.Current.DeclineState;
    rig.Time.Advance(TimeSpan.FromMilliseconds(1));

    Assert.Equal(IncomingCallDeclineState.Declining, justBefore);
    Assert.Equal(IncomingCallDeclineState.Failed, rig.Service.Current.DeclineState);
    Assert.True(rig.Service.Current.IsVisible);
  }

  [Theory]
  [InlineData(HttpStatusCode.InternalServerError, "{\"declined\":false}", "application/json")]
  [InlineData(HttpStatusCode.Conflict, "{\"declined\":false,\"state\":\"InCall\"}", "application/json")]
  [InlineData(HttpStatusCode.OK, "<!doctype html><html></html>", "text/html")]   // an older RotaryPhone's SPA fallback
  [InlineData(HttpStatusCode.NotFound, "{\"error\":\"No API route matches POST /api/phone/decline\"}", "application/json")]   // today's
  [InlineData(HttpStatusCode.OK, "{\"declined\":false}", "application/json")]
  public async Task AFailedDecline_ShowsTheError_LeavesTheBannerUp_AndIgnoreWorksAgain(
    HttpStatusCode status, string body, string contentType)
  {
    var rig = new Rig(declineSupported: true);
    rig.Phone.DeclineResponse = (status, body, contentType);
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);

    await rig.Service.DeclineAsync();
    var failed = rig.Service.Current;
    rig.Phone.DeclineResponse = (HttpStatusCode.OK, "{\"declined\":true}", "application/json");
    await rig.Service.DeclineAsync();

    Assert.True(failed.IsVisible);
    Assert.Equal(IncomingCallPhase.Ringing, failed.Phase);
    Assert.Equal(IncomingCallDeclineState.Failed, failed.DeclineState);
    Assert.Equal(2, rig.Phone.Count("/api/phone/decline"));
    Assert.Equal(IncomingCallDeclineState.Declining, rig.Service.Current.DeclineState);
  }

  [Fact]
  public async Task ALateFailureForAnEarlierCall_DoesNotMarkTheNextOne()
  {
    var rig = new Rig(declineSupported: true);
    var gate = new TaskCompletionSource();
    rig.Phone.DeclineGate = gate.Task;
    rig.Phone.DeclineResponse = (HttpStatusCode.InternalServerError, "{}", "application/json");
    rig.Start();
    rig.Hub.RaiseIncomingCallForTest("default", Number);

    var pending = rig.Service.DeclineAsync();
    rig.Hub.RaiseCallStateChangedForTest("default", "Idle");
    rig.Hub.RaiseIncomingCallForTest("default", "5550100000");
    gate.SetResult();
    await pending;

    Assert.True(rig.Service.Current.IsVisible);
    Assert.Equal(IncomingCallDeclineState.None, rig.Service.Current.DeclineState);
  }

  // ── PHN-5 ─────────────────────────────────────────────────────────

  [Fact]
  public async Task NoLogLine_CarriesTheNumberOrTheName_AndNothingIsLoggedAtInformation()
  {
    var sink = new CapturingSink();
    var rig = new Rig(declineSupported: true, sink: sink);
    rig.Phone.LookupThrowsOnce = true;   // reaches the lookup's failure line, which mentions the number
    // ...and the name then resolves from RotaryPhone's own contacts, so it genuinely exists during the run:
    // without it the "Grandma" sweep below could not fail.
    rig.Phone.ContactsJson = $"[{{\"id\":\"1\",\"name\":\"Grandma Anderson\",\"phoneNumber\":\"{Number}\"}}]";
    rig.Start();

    rig.Hub.RaiseCallStateChangedForTest("default", "Ringing");
    rig.Hub.RaiseIncomingCallForTest("default", Number);
    await rig.Service.LastNameLookup;
    Assert.Equal("Grandma Anderson", rig.Service.Current.PrimaryText);
    rig.Phone.DeclineResponse = (HttpStatusCode.InternalServerError, "{}", "application/json");
    await rig.Service.DeclineAsync();
    rig.Phone.StatusFails = true;
    await rig.Tick();
    rig.Hub.RaiseCallStateChangedForTest("default", "Idle");

    var lines = sink.Lines;
    Assert.NotEmpty(lines);
    foreach (var (level, text) in lines)
    {
      Assert.DoesNotContain(Number, text, StringComparison.Ordinal);
      Assert.DoesNotContain("7424", text, StringComparison.Ordinal);
      Assert.DoesNotContain("Grandma", text, StringComparison.Ordinal);
      if (sink.IsBannerCategory(text))
      {
        Assert.True(level < LogLevel.Information || level >= LogLevel.Warning,
          $"The banner logged at {level}: {text}");
      }
    }
    // Masked, not absent: the lookup failure line is there, carrying the token.
    Assert.Contains(lines, l => l.Text.Contains(LogSafeText.ForPhone(Number), StringComparison.Ordinal));
    Assert.DoesNotContain(lines, l => l.Level == LogLevel.Information && l.Text.Contains("banner", StringComparison.OrdinalIgnoreCase));
  }

  // ── helpers ───────────────────────────────────────────────────────

  [Theory]
  [InlineData("Grandma Anderson", "GA")]
  [InlineData("Cher", "C")]
  [InlineData("  mary   jo  smith ", "MS")]
  [InlineData("Anderson, Carol", "CA")]           // spec §4: "Last, First" reads the given name first
  [InlineData("+1 Work", null)]                   // not a letter first: the host shows the glyph
  [InlineData("(Work) Bob", null)]
  [InlineData("!!!", null)]
  public void Monogram_FollowsTheSpecsInitialsRule(string name, string? expected)
  {
    Assert.Equal(expected, IncomingCallBannerService.MonogramFor(name));
  }

  /// <summary>Captures every line at every level, with its level and the exception text.</summary>
  private sealed class CapturingSink : ILoggerFactory
  {
    private readonly List<(LogLevel Level, string Text)> _lines = [];

    public IReadOnlyList<(LogLevel Level, string Text)> Lines
    {
      get { lock (_lines) { return _lines.ToArray(); } }
    }

    public bool IsBannerCategory(string text) => text.StartsWith("[banner]", StringComparison.Ordinal);

    public ILogger CreateLogger(string categoryName) =>
      new Capturing(_lines, categoryName.EndsWith(nameof(IncomingCallBannerService), StringComparison.Ordinal));

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    private sealed class Capturing(List<(LogLevel, string)> lines, bool banner) : ILogger
    {
      public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
      {
        string prefix = banner ? "[banner] " : "";
        lock (lines)
        {
          lines.Add((logLevel, prefix + formatter(state, exception)));
          if (exception is not null)
          {
            lines.Add((logLevel, prefix + exception));
          }
        }
      }
    }
  }
}
