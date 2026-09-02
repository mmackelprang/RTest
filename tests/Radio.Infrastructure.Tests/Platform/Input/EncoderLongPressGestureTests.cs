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
}
