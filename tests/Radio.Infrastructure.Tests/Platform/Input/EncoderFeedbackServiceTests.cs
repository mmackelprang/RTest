using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers the ENC-4 HUD broadcast coalescer.
///
/// <para>
/// Two properties are being defended here and they pull against each other. The first detent must
/// reach the screen at once — the acceptance criterion is 100 ms and a windowed-only implementation
/// would miss it by the width of the window. A sustained spin must not reach the screen more than
/// 20 times a second — the encoder polls at 10 ms, so an uncoalesced path would fan out at up to
/// 100 Hz onto a Blazor circuit. Leading edge plus trailing flush is what satisfies both.
/// </para>
/// </summary>
public class EncoderFeedbackServiceTests
{
  private static EncoderHudEventArgs Value(int index, string? primary = null) => new()
  {
    EncoderIndex = index,
    Label = "VOLUME",
    Phase = EncoderHudPhase.Value,
    PrimaryText = primary,
  };

  private static EncoderHudEventArgs Hold(int index, EncoderHudPhase phase) => new()
  {
    EncoderIndex = index,
    Label = "VOLUME",
    Phase = phase,
  };

  private static EncoderFeedbackService Create(FakeTimeProvider time) =>
    new(NullLogger<EncoderFeedbackService>.Instance, time);

  [Fact]
  public void FirstValue_EmitsImmediately()
  {
    // The 100 ms requirement. The leading edge must not wait for the coalescing window.
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    var seen = new List<EncoderHudEventArgs>();
    svc.Feedback += (_, e) => seen.Add(e);

    svc.Publish(Value(0, "first"));

    Assert.Single(seen);
    Assert.Equal("first", seen[0].PrimaryText);
  }

  [Fact]
  public void BurstWithinWindow_EmitsLeadingThenOnlyTheFinalValue()
  {
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    var seen = new List<EncoderHudEventArgs>();
    svc.Feedback += (_, e) => seen.Add(e);

    svc.Publish(Value(0, "1"));
    for (int i = 2; i <= 5; i++)
    {
      time.Advance(TimeSpan.FromMilliseconds(10));
      svc.Publish(Value(0, i.ToString()));
    }

    // Past the trailing flush armed by publish #2.
    time.Advance(TimeSpan.FromMilliseconds(60));

    Assert.Equal(2, seen.Count);
    Assert.Equal("1", seen[0].PrimaryText);
    // The pending slot is replaced rather than queued, so the value that lands is the last one.
    Assert.Equal("5", seen[1].PrimaryText);
  }

  [Fact]
  public void BurstAcrossWindows_EmitsAtMostTwentyPerSecond()
  {
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    int raised = 0;
    svc.Feedback += (_, _) => raised++;

    // 100 movements over one second - what a fast spin looks like at PollIntervalMs = 10.
    for (int i = 0; i < 100; i++)
    {
      svc.Publish(Value(0, i.ToString()));
      time.Advance(TimeSpan.FromMilliseconds(10));
    }

    // 20 Hz for one second, plus the leading edge that did not wait for a window.
    Assert.InRange(raised, 2, 21);
  }

  [Fact]
  public void HoldPhases_AreNeverCoalesced()
  {
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    var seen = new List<EncoderHudEventArgs>();
    svc.Feedback += (_, e) => seen.Add(e);

    svc.Publish(Value(0));
    time.Advance(TimeSpan.FromMilliseconds(5));
    svc.Publish(Hold(0, EncoderHudPhase.HoldStart));

    // Well inside the 50 ms window, and it still went out at once: a delayed HoldStart would draw
    // the progress ring late against a threshold the finger is already racing.
    Assert.Equal(2, seen.Count);
    Assert.Equal(EncoderHudPhase.HoldStart, seen[1].Phase);
  }

  [Fact]
  public void HoldPhase_CancelsAPendingValueForThatEncoder()
  {
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    var seen = new List<EncoderHudEventArgs>();
    svc.Feedback += (_, e) => seen.Add(e);

    svc.Publish(Value(0, "leading"));
    time.Advance(TimeSpan.FromMilliseconds(10));
    svc.Publish(Value(0, "pending"));
    time.Advance(TimeSpan.FromMilliseconds(5));
    svc.Publish(Hold(0, EncoderHudPhase.HoldCommit));

    time.Advance(TimeSpan.FromMilliseconds(200));

    Assert.Equal(2, seen.Count);
    Assert.DoesNotContain(seen, e => e.PrimaryText == "pending");
  }

  [Fact]
  public void EncodersCoalesceIndependently()
  {
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    var seen = new List<EncoderHudEventArgs>();
    svc.Feedback += (_, e) => seen.Add(e);

    svc.Publish(Value(0, "enc0-leading"));
    time.Advance(TimeSpan.FromMilliseconds(10));
    svc.Publish(Value(0, "enc0-pending"));
    svc.Publish(Value(3, "enc3"));

    // Two hands on the cabinet is an ordinary case, so encoder 0's window must not hold encoder 3.
    Assert.Equal(2, seen.Count);
    Assert.Equal("enc3", seen[1].PrimaryText);
  }

  [Fact]
  public void SubscriberThrow_DoesNotPropagate()
  {
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    svc.Feedback += (_, _) => throw new InvalidOperationException("boom");

    Assert.Null(Record.Exception(() => svc.Publish(Value(0))));

    // The knobs stay live: a cosmetic subscriber must not take the encoder input path down.
    time.Advance(TimeSpan.FromMilliseconds(200));
    Assert.Null(Record.Exception(() => svc.Publish(Value(0))));
  }

  [Fact]
  public void OutOfRangeIndex_IsDropped()
  {
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    int raised = 0;
    svc.Feedback += (_, _) => raised++;

    svc.Publish(Value(-1));
    svc.Publish(Value(4));
    time.Advance(TimeSpan.FromMilliseconds(200));

    Assert.Equal(0, raised);
  }

  [Fact]
  public void AfterDispose_PublishIsInert()
  {
    var time = new FakeTimeProvider();
    var svc = Create(time);
    int raised = 0;
    svc.Feedback += (_, _) => raised++;

    svc.Dispose();
    svc.Publish(Value(0));
    time.Advance(TimeSpan.FromMilliseconds(200));

    Assert.Equal(0, raised);
  }

  [Fact]
  public void CoalesceWindow_IsTheValueTheHandoffAsksFor()
  {
    // 20 Hz. Pinned because the whole shape of this service follows from it.
    Assert.Equal(50, EncoderInteractionTimings.HudCoalesceMs);
  }

  // --- ENC-5: the selector phases ------------------------------------------------------------

  private static EncoderHudEventArgs Selector(
    int index, EncoderHudPhase phase, int highlight = 0, bool withRows = true) => new()
  {
    EncoderIndex = index,
    Label = "SOURCE",
    Phase = phase,
    HighlightIndex = highlight,
    Rows = withRows
      ? [new EncoderSelectorRow { Id = "band:FM", Primary = "FM" }]
      : null,
  };

  [Fact]
  public void SelectorPreview_IsCoalescedLikeAValue()
  {
    // A moving highlight is a sampled value, not an edge: the knob polls at 10 ms and every sample
    // that reached SignalR would re-render a component tree on the Blazor circuit.
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    var seen = new List<EncoderHudEventArgs>();
    svc.Feedback += (_, e) => seen.Add(e);

    svc.Publish(Selector(1, EncoderHudPhase.SelectorPreview, highlight: 0));
    for (int i = 1; i <= 9; i++)
    {
      time.Advance(TimeSpan.FromMilliseconds(5));
      svc.Publish(Selector(1, EncoderHudPhase.SelectorPreview, highlight: i));
    }

    time.Advance(TimeSpan.FromMilliseconds(60));

    Assert.Equal(2, seen.Count);
    Assert.Equal(0, seen[0].HighlightIndex);
    // The pending slot is replaced rather than queued, so the highlight that lands is the last one.
    Assert.Equal(9, seen[1].HighlightIndex);
  }

  [Fact]
  public void SelectorCommitting_FlushesImmediately_AndClearsAPendingPreview()
  {
    // State D has no timeout on the client, so losing this edge would leave a spinner up forever.
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    var seen = new List<EncoderHudEventArgs>();
    svc.Feedback += (_, e) => seen.Add(e);

    svc.Publish(Selector(1, EncoderHudPhase.SelectorPreview, highlight: 0));
    time.Advance(TimeSpan.FromMilliseconds(10));
    svc.Publish(Selector(1, EncoderHudPhase.SelectorPreview, highlight: 3));
    svc.Publish(Selector(1, EncoderHudPhase.SelectorCommitting, highlight: 3));

    time.Advance(TimeSpan.FromMilliseconds(200));

    Assert.Equal(2, seen.Count);
    Assert.Equal(EncoderHudPhase.SelectorPreview, seen[0].Phase);
    Assert.Equal(EncoderHudPhase.SelectorCommitting, seen[1].Phase);
    // The preview that was pending when the commit arrived is dropped, not emitted afterwards.
    Assert.DoesNotContain(seen.Skip(1), c => c.Phase == EncoderHudPhase.SelectorPreview);
  }

  [Fact]
  public void SelectorPreview_AlwaysCarriesRows()
  {
    // Regression pin for the plan's §1.5: coalescing must pass the payload through untouched, so
    // both the leading emit and the trailing flush still carry the full list. An overlay that
    // received a highlight with no rows would render empty, and only while somebody was spinning.
    var time = new FakeTimeProvider();
    using var svc = Create(time);
    var seen = new List<EncoderHudEventArgs>();
    svc.Feedback += (_, e) => seen.Add(e);

    svc.Publish(Selector(1, EncoderHudPhase.SelectorPreview, highlight: 0));
    time.Advance(TimeSpan.FromMilliseconds(10));
    svc.Publish(Selector(1, EncoderHudPhase.SelectorPreview, highlight: 1));
    time.Advance(TimeSpan.FromMilliseconds(60));

    Assert.Equal(2, seen.Count);
    Assert.All(seen, c => Assert.NotNull(c.Rows));
  }
}
