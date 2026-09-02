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
///   - An unrecognised phase leaves IsHolding where it was — the forward-compatibility rule that
///     lets a newer API build reach an older kiosk without throwing.
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
  public void UnknownPhase_LeavesIsHoldingAlone()
  {
    var clock = new FakeTimeProvider();
    using var svc = NewService(clock);

    svc.Publish(Card("HoldStart"));
    svc.Publish(Card("SomethingENC5WillAdd"));

    // The renderer draws nothing for an unknown phase; the service must not invent a transition
    // out of it either.
    svc.IsHolding.Should().BeTrue();
    svc.Current!.Phase.Should().Be("SomethingENC5WillAdd");
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
