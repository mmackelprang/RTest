using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Unit tests for <see cref="EncoderHudService"/> — the Web-side owner of what the encoder HUD is
/// showing and for how long (ENC-4).
///
/// Contract under test:
///   - Publish sets Current and raises StateChanged.
///   - The card self-dismisses <see cref="EncoderInteractionTimings.HudHoldMs"/> after the last
///     input, and any new input re-arms that timer instead of stacking a second one.
///   - A hold suspends the dismissal entirely, so the progress ring cannot be orphaned by a
///     timeout firing underneath it.
///   - The four-arm phase contract (handoff §6.10): Value preserves IsHolding so a turn mid-hold
///     does not collapse the ring, and every unrecognised phase clears it so a card cannot be
///     stranded on a kiosk by a phase a newer API build invented.
/// </summary>
public class EncoderHudServiceTests
{
  private static EncoderHudDto Card(string phase = "Value", int percent = 50) => new()
  {
    EncoderIndex = 0,
    Label = "VOLUME",
    Phase = phase,
    VolumePercent = percent,
  };

  private static EncoderHudService NewService(FakeTimeProvider clock)
    => new(hub: null, timeProvider: clock);

  /// <summary>
  /// A selector card carrying its own hold duration (ENC-5). <paramref name="durationMs"/> of null
  /// is the case that must fall back to <see cref="EncoderInteractionTimings.HudHoldMs"/>.
  /// </summary>
  private static EncoderHudDto SelectorCard(int? durationMs) => new()
  {
    EncoderIndex = 1,
    Label = "SOURCE",
    Phase = "SelectorPreview",
    Title = "SOURCE",
    HighlightIndex = 0,
    DurationMs = durationMs,
    Rows = [new EncoderSelectorRowDto { Id = "band:FM", Primary = "FM", IsCurrent = true }],
  };

  /// <summary>A commit in flight — State D, which deliberately carries no duration.</summary>
  private static EncoderHudDto CommittingCard() => new()
  {
    EncoderIndex = 1,
    Label = "SOURCE",
    Phase = "SelectorCommitting",
    Title = "SOURCE",
    PrimaryText = "Switching to BLUETOOTH…",
    DurationMs = null,
  };

  [Fact]
  public void Publish_SetsCurrentAndRaisesStateChanged()
  {
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);
    var raised = 0;
    svc.StateChanged += () => raised++;

    svc.Publish(Card(percent: 62));

    svc.Current.Should().NotBeNull();
    svc.Current!.VolumePercent.Should().Be(62);
    raised.Should().Be(1);
  }

  [Fact]
  public void Card_DismissesAfterTheHoldWindow()
  {
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card());
    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs - 1));
    svc.Current.Should().NotBeNull("the hold window has not elapsed yet");

    clock.Advance(TimeSpan.FromMilliseconds(1));
    svc.Current.Should().BeNull();
  }

  [Fact]
  public void NewValueBeforeTimeout_ReArmsWithoutClearing()
  {
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    // Derived from the constant rather than written out. These were literal 1400s, chosen against
    // ENC-4's 1500 ms hold, and ENC-20's raise to 2500 turned the final assertion into a false pass:
    // 1400 + 200 no longer exceeds the window, so the card would have been alive for a reason that
    // had nothing to do with re-arming.
    int justInsideTheWindow = EncoderInteractionTimings.HudHoldMs - 100;

    svc.Publish(Card(percent: 40));
    clock.Advance(TimeSpan.FromMilliseconds(justInsideTheWindow));
    svc.Publish(Card(percent: 41));
    clock.Advance(TimeSpan.FromMilliseconds(justInsideTheWindow));

    // The second detent restarted the window rather than letting the first one expire — twice
    // `justInsideTheWindow` is comfortably past a single hold.
    svc.Current.Should().NotBeNull();
    svc.Current!.VolumePercent.Should().Be(41);

    clock.Advance(TimeSpan.FromMilliseconds(200));
    svc.Current.Should().BeNull();
  }

  [Fact]
  public void HoldStart_SuspendsTheDismissalTimer()
  {
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card("HoldStart"));
    clock.Advance(TimeSpan.FromSeconds(5));

    svc.Current.Should().NotBeNull("a card must not time out from under a progress ring");
    svc.IsHolding.Should().BeTrue();
  }

  [Fact]
  public void HoldCancel_ClearsIsHoldingAndReArmsTheTimer()
  {
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card("HoldStart"));
    clock.Advance(TimeSpan.FromMilliseconds(200));
    svc.Publish(Card("HoldCancel"));

    svc.IsHolding.Should().BeFalse();
    svc.Current.Should().NotBeNull();

    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs));
    svc.Current.Should().BeNull();
  }

  [Fact]
  public void HoldCommit_ClearsIsHolding()
  {
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card("HoldStart"));
    svc.Publish(Card("HoldCommit"));

    svc.IsHolding.Should().BeFalse();
    svc.Current.Should().NotBeNull();
  }

  [Fact]
  public void UnknownPhase_IsNotHolding_SoTheCardCannotBeStranded()
  {
    // ⚠ This test was inverted by handoff §6.10, deliberately and with the reasoning recorded
    // there. It previously asserted that an unknown phase LEAVES IsHolding alone, because "the
    // service must not invent a transition out of it". That was sound about the CARD and wrong
    // about the TIMER: a true IsHolding suspends the HudHoldMs dismissal, so HoldStart → unknown →
    // Value left a card on screen with nothing left to remove it. The renderer still draws
    // nothing for a phase it does not know, which is all the original rule was defending, so the
    // forward-compatibility question this test guards still matters — it just has a different
    // correct answer.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card("HoldStart"));
    svc.IsHolding.Should().BeTrue();

    svc.Publish(Card("SomethingENC5WillAdd"));

    svc.IsHolding.Should().BeFalse();
    svc.Current!.Phase.Should().Be("SomethingENC5WillAdd");
  }

  [Fact]
  public void UnknownPhaseMidHold_StillDismissesOnTime()
  {
    // The behaviour the arm above exists for, rather than the flag that produces it: a card
    // published under a phase this build does not know must still go away by itself.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card("HoldStart"));
    svc.Publish(Card("SomethingENC5WillAdd"));

    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs + 1));

    svc.Current.Should().BeNull("an unrecognised phase must not suspend the dismissal timer");
  }

  [Fact]
  public void ValuePhaseMidHold_PreservesIsHolding()
  {
    // The trap §6.10 calls out: "Value" used to reach the same default arm as an unknown phase, so
    // the obvious fix — flipping that default to false — would have stopped the stranding AND
    // silently broken the hold-and-turn ring. Turning the knob while the button is held publishes
    // a Value card, and the ring has to keep drawing through it.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card("HoldStart"));
    svc.Publish(Card("Value", percent: 63));

    svc.IsHolding.Should().BeTrue();
    svc.Current!.VolumePercent.Should().Be(63);
  }

  [Fact]
  public void ValuePhaseOutsideAHold_LeavesIsHoldingFalse()
  {
    // The other half of "preserved": Value must not manufacture a hold either.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card("Value"));

    svc.IsHolding.Should().BeFalse();
  }

  [Fact]
  public void Dismiss_ClearsImmediately()
  {
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);
    svc.Publish(Card("HoldStart"));

    svc.Dismiss();

    svc.Current.Should().BeNull();
    svc.IsHolding.Should().BeFalse();
  }

  [Fact]
  public void AfterDispose_PublishIsInert()
  {
    var clock = new FakeTimeProvider();
    var svc = NewService(clock);
    svc.Dispose();

    var raised = 0;
    svc.StateChanged += () => raised++;
    svc.Publish(Card());

    svc.Current.Should().BeNull();
    raised.Should().Be(0);
  }

  [Fact]
  public void Dismiss_ClearsACardThatIsMidHold()
  {
    // The functional half of the ENC-0 disconnect teardown. A hold suspends the dismissal timer,
    // and a device that vanishes mid-hold sends no HoldCancel or HoldCommit - so without something
    // able to clear it, the card stays on a kiosk screen indefinitely. The disconnect subscription
    // that calls this is verified by inspection: AudioStateHubService.EncoderConnectionChanged is a
    // field-like event, so a test cannot raise it.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card(phase: "HoldStart"));
    clock.Advance(TimeSpan.FromSeconds(10));
    svc.Current.Should().NotBeNull("a hold suspends the dismissal timer");
    svc.IsHolding.Should().BeTrue();

    svc.Dismiss();

    svc.Current.Should().BeNull();
    svc.IsHolding.Should().BeFalse();
  }

  [Fact]
  public void AThrowingSubscriber_DoesNotPropagate_OnEitherPath()
  {
    // The dismissal timer callback runs with no Blazor or hosting exception boundary above it, so
    // an unhandled throw there would end the process and every circuit in it - not just this card.
    // Mirrors the guard EncoderFeedbackService.Raise already has on the API side.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);
    svc.StateChanged += () => throw new InvalidOperationException("subscriber blew up");

    Record.Exception(() => svc.Publish(Card())).Should().BeNull("the publish path swallows it");

    // Advancing the fake clock runs the timer callback on this thread, so an unguarded throw in
    // Dismiss would surface right here.
    Record.Exception(() => clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs + 50)))
      .Should().BeNull("the dismissal-timer path swallows it too");
  }

  [Fact]
  public void HasRenderableCard_IsFalseForNoCardAndForAPhaseThisBuildCannotDraw()
  {
    // Sleep.razor swaps its clock composition out for the HUD on this flag. Branching on the card's
    // mere presence would hide the clock and then draw nothing in its place, because an
    // unrecognised phase renders nothing - a blank panel rather than a clock.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.HasRenderableCard.Should().BeFalse("nothing has been published yet");

    svc.Publish(Card());
    svc.HasRenderableCard.Should().BeTrue();

    svc.Publish(Card(phase: "SomeFuturePhaseFromANewerApi"));
    svc.Current.Should().NotBeNull("the card is retained");
    svc.HasRenderableCard.Should().BeFalse("this build cannot draw that phase");
  }

  [Fact]
  public void SelectorPreview_HoldsForItsOwnDuration_NotTheDefault()
  {
    // ENC-5 / handoff §6.5: a selector overlay is up for 4000 ms because a list has to be READ,
    // where a value card is glanced at in HudHoldMs. The duration rides on the payload rather than
    // being looked up from the phase, so ENC-7's notices need no change here.
    //
    // Both advances are derived. The first was a literal 1600, sized against ENC-4's 1500 ms hold;
    // ENC-20's raise to 2500 left the assertion passing while its stated reason — "past the default
    // hold" — had become false, which is the failure mode this whole row is about.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(SelectorCard(EncoderInteractionTimings.SelectorIdleDismissMs));

    int pastTheDefaultHold = EncoderInteractionTimings.HudHoldMs + 100;
    clock.Advance(TimeSpan.FromMilliseconds(pastTheDefaultHold));
    svc.Current.Should().NotBeNull("past the default hold but well inside the payload's 4000 ms");

    clock.Advance(TimeSpan.FromMilliseconds(
      EncoderInteractionTimings.SelectorIdleDismissMs - pastTheDefaultHold + 100));
    svc.Current.Should().BeNull("now past the duration the payload asked for");
  }

  [Fact]
  public void NullDuration_FallsBackToTheDefaultHold()
  {
    // Every ENC-4 card sends null here, so this is the arm that keeps the shipped behaviour.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(SelectorCard(durationMs: null));

    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs - 1));
    svc.Current.Should().NotBeNull();

    clock.Advance(TimeSpan.FromMilliseconds(1));
    svc.Current.Should().BeNull();
  }

  [Fact]
  public void CommittingCard_DoesNotDismissAtTheDefaultHold()
  {
    // The regression this pins, found in pre-merge review. SourceSelectorService publishes
    // SelectorCommitting with DurationMs = null ON PURPOSE, because handoff §6.6 State D says the
    // spinner stays up until the switch succeeds or fails. Treating that null as "use the HudHoldMs
    // default" dropped the spinner mid-switch on exactly the slow Bluetooth connect it exists to
    // explain, then flashed it back when the terminal phase arrived.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(CommittingCard());

    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs * 3));
    svc.Current.Should().NotBeNull("a commit in flight outlasts the ordinary card hold");
  }

  [Fact]
  public void CommittingCard_StillDismissesAtTheFailsafeCeiling()
  {
    // The other half, and the reason the fix is a ceiling rather than an infinite hold. The
    // terminal phase travels over SignalR; if the hub drops mid-commit nothing else clears the
    // card. ENC-4 shipped exactly that bug on the hold ring, where a device unplugged mid-hold
    // left a card up indefinitely.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(CommittingCard());

    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.SelectorCommitCeilingMs - 1));
    svc.Current.Should().NotBeNull();

    clock.Advance(TimeSpan.FromMilliseconds(1));
    svc.Current.Should().BeNull("nothing may stay on screen forever");
  }

  [Fact]
  public void ATerminalPhaseAfterACommit_ReArmsAtItsOwnDuration()
  {
    // The normal path: the spinner is replaced by a failure card, which must then dismiss on the
    // failure card's own 4000 ms rather than inheriting the commit ceiling.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(CommittingCard());
    clock.Advance(TimeSpan.FromSeconds(2));

    svc.Publish(SelectorCard(durationMs: EncoderInteractionTimings.SelectorFailedMs));

    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.SelectorFailedMs - 1));
    svc.Current.Should().NotBeNull();

    clock.Advance(TimeSpan.FromMilliseconds(1));
    svc.Current.Should().BeNull();
  }

  [Fact]
  public void ANewPublishReArmsWithTheNewDuration()
  {
    // The ordering this depends on: Publish assigns Current before it arms the timer, so the arm
    // reads the duration of the card being published rather than of the one it replaced. A
    // 4000 ms overlay followed by a value card must dismiss at the card's own HudHoldMs.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(SelectorCard(EncoderInteractionTimings.SelectorIdleDismissMs));
    clock.Advance(TimeSpan.FromMilliseconds(500));

    svc.Publish(Card());
    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs - 1));
    svc.Current.Should().NotBeNull();

    clock.Advance(TimeSpan.FromMilliseconds(2));
    svc.Current.Should().BeNull("the value card re-armed at its own HudHoldMs, not the overlay's 4000");
  }

  [Fact]
  public void CurrentIsTheSignalThatAHandIsOnAKnob_NotThatSomethingHappened()
  {
    // ENC-20. MainLayout subscribes to StateChanged to undim the screen and reset idle-dimmer.js's
    // dim and sleep timers, because encoder input arrives as a SignalR push and dispatches no DOM
    // event for the dimmer to hear. It guards that wake on `Current is not null`, and this pins the
    // predicate the guard is built on.
    //
    // ⚠ The guard is load-bearing, not defensive. StateChanged fires on Dismiss() too — the hold
    // timer expiring, and the device DISCONNECTING — and waking on those would reset the five-minute
    // idle countdown moments after the user stopped touching anything, or let unplugging the encoder
    // count as a human being present. Only a non-null Current means a card is on screen because a
    // hand is on a knob.
    //
    // Asserted here rather than through MainLayout: that component is not renderable under bUnit
    // (see MainLayoutTests — Radzen dropdowns, JSInterop and the full API service graph), and
    // contorting it into a harness for this would test the harness.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Current.Should().BeNull("nothing has been published yet, so no wake may be triggered");

    svc.Publish(Card());
    svc.Current.Should().NotBeNull("a published card is the state that means a knob is being used");

    svc.Dismiss();
    svc.Current.Should().BeNull(
      "Dismiss is the hold timer expiring or the device disconnecting — neither is user activity");
  }

  [Fact]
  public async Task DependencyInjection_ResolvesOneInstanceWithTheHubInjected()
  {
    // Program.cs registers this with a bare AddSingleton<EncoderHudService>(), which only works
    // because the container fills the hub from DI and takes the compile-time default for the
    // TimeProvider that nothing registers. This is the cheap standing check for that, and for the
    // singleton lifetime the two hosts depend on to agree about one physical cabinet.
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    services.AddSingleton<ILogger<AudioStateHubService>>(NullLogger<AudioStateHubService>.Instance);
    services.AddSingleton<AudioStateHubService>();
    services.AddSingleton<EncoderHudService>();

    // await using, not using: AudioStateHubService is IAsyncDisposable only, and a synchronous
    // container teardown throws on it.
    await using var provider = services.BuildServiceProvider();

    var first = provider.GetRequiredService<EncoderHudService>();
    provider.GetRequiredService<EncoderHudService>().Should().BeSameAs(first);
  }
}
