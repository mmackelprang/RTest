using Bunit;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using Radio.Core.Configuration;
using Radio.Web.Components.Shared;
using Xunit;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for <see cref="SleepPill"/> (<c>ENC-23</c>): a tap enters Standby exactly as the pill
/// always did; a hold of <see cref="EncoderInteractionTimings.LongPressThresholdMs"/>, resolved on
/// lift, enters deep sleep and swallows the click that trails it.
///
/// <para>
/// The hold is measured on a <see cref="FakeTimeProvider"/> handed in through
/// <see cref="SleepPill.Clock"/>, so every case is a clock advance rather than a wall-clock wait
/// (CLAUDE.md § Test Timing).
/// </para>
/// </summary>
public class SleepPillTests : TestContext
{
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
  private int _sleeps;
  private int _deepSleeps;

  public SleepPillTests()
  {
    Services.AddRadzenComponents();
  }

  private IRenderedComponent<SleepPill> Render() =>
    RenderComponent<SleepPill>(p => p
      .Add(x => x.OnSleep, () => _sleeps++)
      .Add(x => x.OnDeepSleep, () => _deepSleeps++)
      .Add(x => x.Clock, _clock));

  /// <summary>One press, as a touchscreen delivers it: down, held for <paramref name="heldMs"/>, up, then click.</summary>
  private void Press(IRenderedComponent<SleepPill> cut, int heldMs, bool click = true)
  {
    cut.Find("button").PointerDown();
    _clock.Advance(TimeSpan.FromMilliseconds(heldMs));
    cut.Find("button").PointerUp();
    if (click)
    {
      cut.Find("button").Click();
    }
  }

  [Fact]
  public void Tap_EntersSleep_NotDeepSleep()
  {
    var cut = Render();

    Press(cut, 100);

    Assert.Equal(1, _sleeps);
    Assert.Equal(0, _deepSleeps);
  }

  [Fact]
  public void HoldToTheThreshold_EntersDeepSleep_AndSwallowsTheTrailingClick()
  {
    var cut = Render();

    Press(cut, EncoderInteractionTimings.LongPressThresholdMs);

    Assert.Equal(1, _deepSleeps);
    Assert.Equal(0, _sleeps);
  }

  [Fact]
  public void HoldOneMillisecondShort_IsATap()
  {
    var cut = Render();

    Press(cut, EncoderInteractionTimings.LongPressThresholdMs - 1);

    Assert.Equal(1, _sleeps);
    Assert.Equal(0, _deepSleeps);
  }

  [Fact]
  public void DeepSleep_FiresOnLift_NotWhileStillHeld()
  {
    var cut = Render();

    cut.Find("button").PointerDown();
    _clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.LongPressThresholdMs * 3));
    Assert.Equal(0, _deepSleeps);

    cut.Find("button").PointerUp();
    Assert.Equal(1, _deepSleeps);
  }

  [Fact]
  public void PointerCancel_ThenUp_DoesNothingDeep()
  {
    var cut = Render();

    cut.Find("button").PointerDown();
    _clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.LongPressThresholdMs));
    cut.Find("button").PointerCancel();
    cut.Find("button").PointerUp();

    Assert.Equal(0, _deepSleeps);
    Assert.Equal(0, _sleeps);
  }

  [Fact]
  public void PointerLeave_ThenUp_DoesNothingDeep()
  {
    var cut = Render();

    cut.Find("button").PointerDown();
    _clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.LongPressThresholdMs));
    cut.Find("button").PointerLeave();
    cut.Find("button").PointerUp();

    Assert.Equal(0, _deepSleeps);
  }

  [Fact]
  public void PointerLeaveBetweenALiftAndItsClick_StillSwallowsTheClick()
  {
    // Touch delivers pointerleave after pointerup and before the click. The hold has already resolved;
    // the leave must not clear the suppression, or the click would enter plain Standby on top of it.
    var cut = Render();

    cut.Find("button").PointerDown();
    _clock.Advance(TimeSpan.FromMilliseconds(EncoderInteractionTimings.LongPressThresholdMs));
    cut.Find("button").PointerUp();
    cut.Find("button").PointerLeave();
    cut.Find("button").Click();

    Assert.Equal(1, _deepSleeps);
    Assert.Equal(0, _sleeps);
  }

  [Fact]
  public void KeyboardActivation_WithNoPointerEvents_EntersSleep()
  {
    var cut = Render();

    cut.Find("button").Click();

    Assert.Equal(1, _sleeps);
    Assert.Equal(0, _deepSleeps);
  }

  [Fact]
  public void IsHolding_IsSetOnDown_AndClearedOnUp()
  {
    var cut = Render();
    Assert.DoesNotContain("is-holding", cut.Find("button").ClassList);

    cut.Find("button").PointerDown();
    Assert.Contains("is-holding", cut.Find("button").ClassList);

    cut.Find("button").PointerUp();
    Assert.DoesNotContain("is-holding", cut.Find("button").ClassList);
  }

  [Fact]
  public void IsHolding_IsClearedOnCancel()
  {
    var cut = Render();

    cut.Find("button").PointerDown();
    cut.Find("button").PointerCancel();

    Assert.DoesNotContain("is-holding", cut.Find("button").ClassList);
  }

  [Fact]
  public void AStaleSuppression_IsClearedByTheNextPress_SoTheNextTapSleeps()
  {
    var cut = Render();

    // A hold whose trailing click never arrived.
    Press(cut, EncoderInteractionTimings.LongPressThresholdMs, click: false);
    Assert.Equal(1, _deepSleeps);

    Press(cut, 100);

    Assert.Equal(1, _sleeps);
    Assert.Equal(1, _deepSleeps);
  }

  [Fact]
  public void Label_AndAccessibleName()
  {
    var cut = Render();

    Assert.Equal("Sleep", cut.Find(".nav-pill-label").TextContent);
    Assert.Equal("Enter sleep mode. Hold to also turn the screen off", cut.Find("button").GetAttribute("aria-label"));
    Assert.Equal("Sleep (hold to turn the screen off)", cut.Find("button").GetAttribute("title"));
    Assert.Equal("true", cut.Find(".nav-pill-sleep-fill").GetAttribute("aria-hidden"));
  }
}
