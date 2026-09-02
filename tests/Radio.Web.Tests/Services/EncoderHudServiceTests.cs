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
  public void Card_DismissesAfterFifteenHundredMilliseconds()
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

    svc.Publish(Card(percent: 40));
    clock.Advance(TimeSpan.FromMilliseconds(1400));
    svc.Publish(Card(percent: 41));
    clock.Advance(TimeSpan.FromMilliseconds(1400));

    // The second detent restarted the window rather than letting the first one expire.
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
    // about the TIMER: a true IsHolding suspends the 1500 ms dismissal, so HoldStart → unknown →
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
    // where a value card is glanced at in 1500 ms. The duration rides on the payload rather than
    // being looked up from the phase, so ENC-7's notices need no change here.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(SelectorCard(EncoderInteractionTimings.SelectorIdleDismissMs));

    clock.Advance(TimeSpan.FromMilliseconds(1600));
    svc.Current.Should().NotBeNull("1600 ms is past the default hold but well inside 4000 ms");

    clock.Advance(TimeSpan.FromMilliseconds(2500));
    svc.Current.Should().BeNull("4100 ms is past the duration the payload asked for");
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
  public void ANewPublishReArmsWithTheNewDuration()
  {
    // The ordering this depends on: Publish assigns Current before it arms the timer, so the arm
    // reads the duration of the card being published rather than of the one it replaced. A
    // 4000 ms overlay followed by a 1500 ms value card must dismiss at 1500.
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(SelectorCard(EncoderInteractionTimings.SelectorIdleDismissMs));
    clock.Advance(TimeSpan.FromMilliseconds(500));

    svc.Publish(Card());
    clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs - 1));
    svc.Current.Should().NotBeNull();

    clock.Advance(TimeSpan.FromMilliseconds(2));
    svc.Current.Should().BeNull("the value card re-armed at its own 1500 ms, not the overlay's 4000");
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
