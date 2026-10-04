using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Xunit;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// PHN-13: the Phone page hero's <b>Ignore</b> (was a disabled <b>Reject</b>) declines the ringing call — the same
/// decline as the banner's Ignore. A <c>409</c> (answered first, or the caller gave up) resets quietly; only a real
/// failure shows "Couldn't end the call. Try again.".
/// </summary>
/// <remarks>
/// No wall clock: the decline is a <see cref="TaskCompletionSource{TResult}"/> the test completes, and the
/// deadline runs on a <see cref="FakeTimeProvider"/>. The one <c>WaitForAssertion</c> waits for the render a
/// fake-timer callback queues — a rendezvous on the observation, bounded, not a race.
/// </remarks>
public class PhoneStatusHeroIgnoreTests : TestContext
{
  private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-03T12:00:00Z"));
  private int _declines;

  public PhoneStatusHeroIgnoreTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private IRenderedComponent<PhoneStatusHero> Render(
    Func<Task<DeclineCallOutcome>>? onIgnore, bool canIgnore = true, string callState = "Ringing") =>
    RenderComponent<PhoneStatusHero>(p => p
      .Add(x => x.CallState, new PhoneCallStateDto { CallState = callState, IncomingNumber = "9193718044" })
      .Add(x => x.CanIgnore, canIgnore)
      .Add(x => x.OnIgnore, onIgnore is null ? null : () =>
      {
        _declines++;
        return onIgnore();
      })
      .Add(x => x.Time, _time));

  private static AngleSharp.Dom.IElement Ignore(IRenderedComponent<PhoneStatusHero> cut) =>
    cut.Find(".phone-hero-ignore");

  [Fact]
  public void Ringing_ShowsIgnore_NotReject()
  {
    var cut = Render(() => Task.FromResult(DeclineCallOutcome.Declined));

    Ignore(cut).TextContent.Trim().Should().EndWith("Ignore");
    cut.Markup.Should().NotContain("Reject");
    Ignore(cut).HasAttribute("disabled").Should().BeFalse();
  }

  [Fact]
  public void WithDeclineNotSupported_IgnoreIsDisabled_AndSaysWhy()
  {
    var cut = Render(() => Task.FromResult(DeclineCallOutcome.Declined), canIgnore: false);

    Ignore(cut).HasAttribute("disabled").Should().BeTrue();
    Ignore(cut).GetAttribute("title").Should().StartWith("Not available yet");
    Ignore(cut).Click();
    _declines.Should().Be(0);
  }

  [Fact]
  public void AnAcceptedDecline_ShowsEndingCall_UntilTheCallStopsRinging()
  {
    var cut = Render(() => Task.FromResult(DeclineCallOutcome.Declined));

    Ignore(cut).Click();

    Ignore(cut).TextContent.Should().Contain("Ending call…");
    Ignore(cut).HasAttribute("disabled").Should().BeTrue();
    _declines.Should().Be(1);

    cut.SetParametersAndRender(p => p.Add(x => x.CallState, new PhoneCallStateDto { CallState = "Idle" }));
    cut.FindAll(".phone-hero-ignore").Should().BeEmpty();   // Idle shows no Ignore at all
    cut.FindAll(".phone-hero-ignore-error").Should().BeEmpty();
  }

  [Fact]
  public void ADoubleTap_SendsOneDecline()
  {
    var pending = new TaskCompletionSource<DeclineCallOutcome>();
    var cut = Render(() => pending.Task);

    Ignore(cut).Click();
    Ignore(cut).Click();   // disabled while in flight; even a click that lands must not send a second

    _declines.Should().Be(1);
    pending.SetResult(DeclineCallOutcome.Declined);
  }

  [Theory]
  [InlineData("InCall")]   // the handset was lifted first
  [InlineData("Idle")]     // the caller gave up
  public void A409_ResetsQuietly_WithNoError(string state)
  {
    var cut = Render(() => Task.FromResult(new DeclineCallOutcome(DeclineCallResult.NotRinging, state)));

    Ignore(cut).Click();

    cut.FindAll(".phone-hero-ignore-error").Should().BeEmpty();
    Ignore(cut).TextContent.Trim().Should().EndWith("Ignore");
    Ignore(cut).HasAttribute("disabled").Should().BeFalse();
    // And the deadline that a 200 would have started never fires an error either.
    _time.Advance(PhoneStatusHero.IgnoreDeadline);
    cut.FindAll(".phone-hero-ignore-error").Should().BeEmpty();
  }

  [Fact]
  public void AFailure_ShowsTheError_AndIgnoreWorksAgain()
  {
    var outcome = DeclineCallOutcome.Failed;
    var cut = Render(() => Task.FromResult(outcome));

    Ignore(cut).Click();

    var error = cut.Find(".phone-hero-ignore-error");
    error.TextContent.Should().Be("Couldn't end the call. Try again.");
    error.GetAttribute("role").Should().Be("alert");
    Ignore(cut).HasAttribute("disabled").Should().BeFalse();

    outcome = DeclineCallOutcome.Declined;
    Ignore(cut).Click();
    _declines.Should().Be(2);
    cut.FindAll(".phone-hero-ignore-error").Should().BeEmpty();
  }

  [Fact]
  public void AnAcceptedDecline_StillRingingAfterTheDeadline_ShowsTheError()
  {
    var cut = Render(() => Task.FromResult(DeclineCallOutcome.Declined));
    Ignore(cut).Click();

    _time.Advance(PhoneStatusHero.IgnoreDeadline - TimeSpan.FromMilliseconds(1));
    cut.FindAll(".phone-hero-ignore-error").Should().BeEmpty();
    _time.Advance(TimeSpan.FromMilliseconds(1));

    cut.WaitForAssertion(() =>
      cut.Find(".phone-hero-ignore-error").TextContent.Should().Be("Couldn't end the call. Try again."));
    Ignore(cut).HasAttribute("disabled").Should().BeFalse();
  }

  [Fact]
  public void TheCallStoppingRinging_BeforeTheDeadline_CancelsIt()
  {
    var cut = Render(() => Task.FromResult(DeclineCallOutcome.Declined));
    Ignore(cut).Click();

    cut.SetParametersAndRender(p => p.Add(x => x.CallState, new PhoneCallStateDto { CallState = "Idle" }));
    _time.Advance(PhoneStatusHero.IgnoreDeadline);
    // A new ring afterwards starts clean: no error carried over, Ignore available.
    cut.SetParametersAndRender(p => p.Add(x => x.CallState,
      new PhoneCallStateDto { CallState = "Ringing", IncomingNumber = "9193718044" }));

    cut.FindAll(".phone-hero-ignore-error").Should().BeEmpty();
    Ignore(cut).TextContent.Trim().Should().EndWith("Ignore");
    Ignore(cut).HasAttribute("disabled").Should().BeFalse();
  }
}
