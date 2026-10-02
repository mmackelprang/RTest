using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers the host-side long-press synthesis.
///
/// <para>
/// The device reports raw press and release edges and has no long-press gesture, so every rule
/// exercised here is one this class invents. The two that carry behaviour: the short action fires on
/// <b>release</b>, and the long action fires <b>at</b> the threshold while the button is still held,
/// after which the release is inert.
/// </para>
/// </summary>
public class EncoderLongPressGestureTests
{
  private sealed class Recorder
  {
    public readonly List<int> ShortPress = [];
    public readonly List<int> LongPress = [];
    public readonly List<int> HoldStarted = [];
    public readonly List<int> HoldCancelled = [];

    public void Attach(EncoderLongPressGesture g)
    {
      g.ShortPress += ShortPress.Add;
      g.LongPress += LongPress.Add;
      g.HoldStarted += HoldStarted.Add;
      g.HoldCancelled += HoldCancelled.Add;
    }
  }

  private static EncoderLongPressGesture Create(FakeTimeProvider time, out Recorder recorder)
  {
    var gesture = new EncoderLongPressGesture(4, NullLogger.Instance, time);
    recorder = new Recorder();
    recorder.Attach(gesture);
    return gesture;
  }

  [Fact]
  public void ShortPress_IsRaisedBeforeHoldCancelled()
  {
    // Regression guard for a shipped defect, and the ordering is behaviour rather than style. The
    // router publishes the HUD card from its HoldCancelled handler, reading the console's mute
    // state as it does so, while the short action on the volume knob is what toggles that state.
    // With the old order the card asserted the pre-toggle value and nothing corrected it, so the
    // HUD showed the opposite of the truth for the card's whole lifetime.
    var time = new FakeTimeProvider();
    var order = new List<string>();
    using var gesture = new EncoderLongPressGesture(4, NullLogger.Instance, time);
    gesture.ShortPress += _ => order.Add("short");
    gesture.HoldCancelled += _ => order.Add("cancel");

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(200));
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { "short", "cancel" }, order);
  }

  [Fact]
  public void PressThenQuickRelease_FiresShortPressOnly()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(200));
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0 }, rec.HoldStarted);
    Assert.Equal(new[] { 0 }, rec.HoldCancelled);
    Assert.Equal(new[] { 0 }, rec.ShortPress);
    Assert.Empty(rec.LongPress);
  }

  [Fact]
  public void ShortPress_FiresOnReleaseNotOnPress()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(200));

    // Firing on press would fire the short action on the way into every hold.
    Assert.Empty(rec.ShortPress);
  }

  [Fact]
  public void HoldToThreshold_FiresLongPressWhileStillHeld()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(600));

    // No release has been fed in, and the action has already happened - that is what lets the ring
    // complete and the thing happen together.
    Assert.Equal(new[] { 0 }, rec.LongPress);
  }

  [Fact]
  public void ReleaseAfterLongPress_DoesNotAlsoFireShortPress()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(600));
    gesture.OnButtonEdge(0, false);

    // The rule that stops hold-for-standby from also muting the console on the way out.
    Assert.Equal(new[] { 0 }, rec.LongPress);
    Assert.Empty(rec.ShortPress);
    Assert.Empty(rec.HoldCancelled);
  }

  [Fact]
  public void ReleaseAtExactlyTheThreshold_PrefersTheLongAction()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(600));
    gesture.OnButtonEdge(0, false);

    Assert.Single(rec.LongPress);
    Assert.Empty(rec.ShortPress);
  }

  [Fact]
  public void Repeat_HoldThenShort_BothBehaveCorrectly()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(600));
    gesture.OnButtonEdge(0, false);

    time.Advance(TimeSpan.FromMilliseconds(100));

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(200));
    gesture.OnButtonEdge(0, false);

    // The second gesture is not poisoned by the first: the long-fired flag was cleared on release.
    Assert.Single(rec.LongPress);
    Assert.Equal(new[] { 0 }, rec.ShortPress);
    Assert.Equal(2, rec.HoldStarted.Count);
  }

  [Fact]
  public void EncodersAreIndependent()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(100));
    gesture.OnButtonEdge(2, true);
    time.Advance(TimeSpan.FromMilliseconds(100));
    gesture.OnButtonEdge(2, false);
    time.Advance(TimeSpan.FromMilliseconds(500));

    Assert.Equal(new[] { 2 }, rec.ShortPress);
    Assert.Equal(new[] { 0 }, rec.LongPress);
  }

  [Fact]
  public void DuplicatePressEdge_DoesNotStackTimers()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(600));

    Assert.Single(rec.LongPress);
    Assert.Single(rec.HoldStarted);
  }

  [Fact]
  public void Dispose_CancelsAPendingHold()
  {
    var time = new FakeTimeProvider();
    var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.Dispose();
    time.Advance(TimeSpan.FromMilliseconds(1000));

    Assert.Empty(rec.LongPress);
  }

  [Fact]
  public void ReleaseWithoutAPress_IsIgnored()
  {
    // The sleep-wake path consumes the press edge, so the release arrives at a gesture that never
    // saw a press. It must not synthesise a short action out of it.
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, false);

    Assert.Empty(rec.ShortPress);
    Assert.Empty(rec.HoldCancelled);
    Assert.Empty(rec.HoldStarted);
  }

  // --- ENC-24: a turn while held cancels the hold ------------------------------------------

  [Fact]
  public void TurnDuringHold_CancelsIt_NeitherLongNorShortFires()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(200));
    gesture.OnTurn(0, 2);
    time.Advance(TimeSpan.FromMilliseconds(ThresholdMs + 400));

    // Holding VOLUME while adjusting it must not drop the console into Standby.
    Assert.Empty(rec.LongPress);

    gesture.OnButtonEdge(0, false);

    // ...and releasing a press-and-turn must not toggle mute.
    Assert.Empty(rec.ShortPress);
    Assert.Equal(new[] { 0 }, rec.HoldCancelled);
  }

  [Fact]
  public void SeveralTurnsDuringHold_RaiseHoldCancelledOnce()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    // One detent at a time: the second reaches the tolerance and cancels, the third finds no hold.
    gesture.OnTurn(0, 1);
    gesture.OnTurn(0, 1);
    gesture.OnTurn(0, 1);
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0 }, rec.HoldCancelled);
    Assert.Empty(rec.ShortPress);
  }

  [Fact]
  public void TurnOnAnotherIndex_DoesNotCancelTheHold()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(1, 2);
    time.Advance(TimeSpan.FromMilliseconds(ThresholdMs));

    Assert.Equal(new[] { 0 }, rec.LongPress);
    Assert.Empty(rec.HoldCancelled);
  }

  [Fact]
  public void TurnOnAnotherIndex_DoesNotCancelAShortPress()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(3, 2);
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0 }, rec.ShortPress);
  }

  [Fact]
  public void TurnAfterLongPressFired_ChangesNothing()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(ThresholdMs));
    gesture.OnTurn(0, 2);
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0 }, rec.LongPress);
    Assert.Empty(rec.ShortPress);
    // The release is still the inert post-long release, and the turn raised no cancel of its own.
    Assert.Empty(rec.HoldCancelled);
  }

  [Fact]
  public void TurnWithNoPress_IsANoOp()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnTurn(0, 2);
    gesture.OnTurn(-1, 2);
    gesture.OnTurn(99, 2);
    time.Advance(TimeSpan.FromMilliseconds(1000));

    Assert.Empty(rec.HoldCancelled);
    Assert.Empty(rec.ShortPress);
    Assert.Empty(rec.LongPress);
  }

  [Fact]
  public void AfterATurnCancel_AFreshPressAndReleaseIsANormalShortPress()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, 2);
    gesture.OnButtonEdge(0, false);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(200));
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0 }, rec.ShortPress);
    Assert.Equal(2, rec.HoldStarted.Count);
    Assert.Equal(2, rec.HoldCancelled.Count);
    Assert.Empty(rec.LongPress);
  }

  [Fact]
  public void AfterATurnCancel_AFreshHoldStillFiresTheLongAction()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, 2);
    gesture.OnButtonEdge(0, false);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(ThresholdMs));

    Assert.Equal(new[] { 0 }, rec.LongPress);
  }

  [Fact]
  public void ATurnCancelledHoldsTimer_CannotFireIntoTheNextPress()
  {
    // The cancelled press's timer must be disposed, not merely outrun. Left armed, it fires at the
    // FIRST press's deadline, finds the SECOND press down, and raises the long action 400 ms into a
    // press that has not reached the threshold.
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(100));
    gesture.OnTurn(0, 2);
    gesture.OnButtonEdge(0, false);
    time.Advance(TimeSpan.FromMilliseconds(100));

    gesture.OnButtonEdge(0, true);
    time.Advance(TimeSpan.FromMilliseconds(ThresholdMs - 200));
    Assert.Empty(rec.LongPress);

    time.Advance(TimeSpan.FromMilliseconds(200));
    Assert.Equal(new[] { 0 }, rec.LongPress);
  }

  // --- ENC-24 tolerance: one stray detent is not a turn -------------------------------------

  [Fact]
  public void OneDetentDuringHold_DoesNotCancel_TheLongActionStillFires()
  {
    // The HID parse raises a report's button edges before its turn, so a push that jostles the knob
    // by one detent arrives as a turn of a held knob. It must not eat the press.
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, 1);
    time.Advance(TimeSpan.FromMilliseconds(ThresholdMs));

    Assert.Equal(new[] { 0 }, rec.LongPress);
    Assert.Empty(rec.HoldCancelled);
  }

  [Fact]
  public void OneDetentDuringAShortPress_DoesNotCancel_TheShortActionStillFiresOnRelease()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, -1);
    time.Advance(TimeSpan.FromMilliseconds(200));
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0 }, rec.ShortPress);
    Assert.Equal(new[] { 0 }, rec.HoldCancelled);
    Assert.Empty(rec.LongPress);
  }

  [Fact]
  public void AWobbleThatNetsToZero_DoesNotCancel()
  {
    // Four detents of travel, but back and forth: the count is signed and net, not total.
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, 1);
    gesture.OnTurn(0, -1);
    gesture.OnTurn(0, 1);
    gesture.OnTurn(0, -1);
    time.Advance(TimeSpan.FromMilliseconds(ThresholdMs));

    Assert.Equal(new[] { 0 }, rec.LongPress);
    Assert.Empty(rec.HoldCancelled);
  }

  [Fact]
  public void TwoSingleDetentsTheSameWay_Cancel()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, -1);
    Assert.Empty(rec.HoldCancelled);
    gesture.OnTurn(0, -1);
    time.Advance(TimeSpan.FromMilliseconds(ThresholdMs));
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0 }, rec.HoldCancelled);
    Assert.Empty(rec.LongPress);
    Assert.Empty(rec.ShortPress);
  }

  [Fact]
  public void TheTurnCount_StartsAgainAtEachPress()
  {
    // One detent in each of two presses is two stray detents, not a turn of two.
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, 1);
    gesture.OnButtonEdge(0, false);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, 1);
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0, 0 }, rec.ShortPress);
  }

  [Fact]
  public void TurnsWithTheButtonUp_DoNotCountTowardTheNextPress()
  {
    var time = new FakeTimeProvider();
    using var gesture = Create(time, out var rec);

    gesture.OnTurn(0, 5);
    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, 1);
    gesture.OnButtonEdge(0, false);

    Assert.Equal(new[] { 0 }, rec.ShortPress);
  }

  // --- A threshold callback from an earlier press ---------------------------------------------

  /// <summary>
  /// A <see cref="TimeProvider"/> whose timers never fire on their own: each one records its callback
  /// and state, and the test invokes them. Disposing one does not stop a test from invoking it, which
  /// is exactly the case under test - a real timer-queue callback already dispatched when the timer
  /// was disposed. <see cref="FakeTimeProvider"/> cannot reproduce that: it never runs a disposed timer.
  /// </summary>
  private sealed class CapturingTimeProvider : TimeProvider
  {
    public readonly List<(TimerCallback Callback, object? State)> Timers = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
      Timers.Add((callback, state));
      return new InertTimer();
    }

    private sealed class InertTimer : ITimer
    {
      public bool Change(TimeSpan dueTime, TimeSpan period) => true;
      public void Dispose() { }
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }

  [Fact]
  public void AnEarlierPresssThresholdCallback_ArrivingLate_DoesNotFireIntoTheNextPress()
  {
    var time = new CapturingTimeProvider();
    using var gesture = new EncoderLongPressGesture(4, NullLogger.Instance, time);
    var rec = new Recorder();
    rec.Attach(gesture);

    gesture.OnButtonEdge(0, true);
    gesture.OnButtonEdge(0, false);
    gesture.OnButtonEdge(0, true);
    Assert.Equal(2, time.Timers.Count);

    // The first press's callback, dispatched before its release disposed the timer, runs now.
    time.Timers[0].Callback(time.Timers[0].State);
    Assert.Empty(rec.LongPress);

    // The second press's own callback still works: the stale one did not touch its timer or state.
    time.Timers[1].Callback(time.Timers[1].State);
    Assert.Equal(new[] { 0 }, rec.LongPress);
  }

  [Fact]
  public void ATurnCancelledPresssThresholdCallback_ArrivingLate_DoesNotFireIntoTheNextPress()
  {
    var time = new CapturingTimeProvider();
    using var gesture = new EncoderLongPressGesture(4, NullLogger.Instance, time);
    var rec = new Recorder();
    rec.Attach(gesture);

    gesture.OnButtonEdge(0, true);
    gesture.OnTurn(0, 2);
    gesture.OnButtonEdge(0, false);
    gesture.OnButtonEdge(0, true);

    time.Timers[0].Callback(time.Timers[0].State);

    Assert.Empty(rec.LongPress);
  }

  private const int ThresholdMs = Radio.Core.Configuration.EncoderInteractionTimings.LongPressThresholdMs;
}
