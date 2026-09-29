using Bunit;
using Microsoft.AspNetCore.Components;
using Radio.Web.Components.Shared;
using Xunit;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// SeekBar — the AUD-28 owner ruling (2026-09-25): SEEK ON RELEASE, no debounce.
/// </summary>
/// <remarks>
/// The gesture itself lives in <c>js/seek-bar.js</c>, which bUnit cannot run. These tests drive the
/// component through the interop seam instead — the three <c>[JSInvokable]</c> methods the module calls:
/// <c>OnDragMove</c> (pointer down / moved while down), <c>OnDragEnd</c> (released) and
/// <c>OnDragCancel</c>. A tap reaches the component as one <c>OnDragMove</c> then one <c>OnDragEnd</c>,
/// which is exactly how the module reports it.
/// </remarks>
public class SeekBarTests : TestContext
{
  private readonly List<double> _seeks = [];
  private readonly List<double?> _scrubs = [];

  public SeekBarTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private IRenderedComponent<SeekBar> Render(double fraction = 0.1, bool disabled = false) =>
    RenderComponent<SeekBar>(p => p
      .Add(x => x.Fraction, fraction)
      .Add(x => x.Disabled, disabled)
      .Add(x => x.OnSeek, EventCallback.Factory.Create<double>(this, f => _seeks.Add(f)))
      .Add(x => x.OnScrub, EventCallback.Factory.Create<double?>(this, f => _scrubs.Add(f))));

  [Fact]
  public async Task Drag_SendsNoSeekWhileMoving_AndExactlyOneOnRelease()
  {
    var cut = Render();

    // ⚠ THIS IS THE ROW. Before AUD-28 every one of these frames was an engine seek.
    foreach (var f in new[] { 0.20, 0.30, 0.40, 0.50, 0.60 })
    {
      await cut.Instance.OnDragMove(f);
    }

    Assert.Empty(_seeks);

    await cut.Instance.OnDragEnd(0.65);

    Assert.Equal([0.65], _seeks);
  }

  [Fact]
  public async Task Tap_StillSeeksExactlyOnce()
  {
    var cut = Render();

    await cut.Instance.OnDragMove(0.25);
    await cut.Instance.OnDragEnd(0.25);

    Assert.Equal([0.25], _seeks);
  }

  [Fact]
  public async Task Scrub_ReportsTheFingerForTheReadout_ThenNullWhenTheDragEnds()
  {
    var cut = Render();

    await cut.Instance.OnDragMove(0.3);
    await cut.Instance.OnDragMove(0.4);
    await cut.Instance.OnDragEnd(0.45);

    // The last scrub is null, sent AFTER the commit, so a parent's readout falls back to real
    // position only once the seek has been sent.
    Assert.Equal([0.3, 0.4, null], _scrubs);
  }

  [Fact]
  public async Task WhileDragging_TheThumbFollowsTheFinger_NotTheParentsPosition()
  {
    var cut = Render(fraction: 0.1);

    await cut.Instance.OnDragMove(0.5);

    // A parent re-render with the old position (the 1 Hz layout tick, a poll) must not yank the
    // thumb back mid-drag.
    cut.SetParametersAndRender(p => p.Add(x => x.Fraction, 0.12));

    Assert.Contains("width:50%", cut.Find(".seek-bar-fill").GetAttribute("style"));
    Assert.Contains("left:50%", cut.Find(".seek-bar-thumb").GetAttribute("style"));
    Assert.Contains("is-dragging", cut.Find(".seek-bar").ClassName);

    await cut.Instance.OnDragEnd(0.5);

    // After the commit the bar shows the parameter again, and the dragging state is gone.
    Assert.Contains("width:12%", cut.Find(".seek-bar-fill").GetAttribute("style"));
    Assert.DoesNotContain("is-dragging", cut.Find(".seek-bar").ClassName);
  }

  [Fact]
  public async Task Cancel_SendsNoSeek_EvenIfAStrayReleaseFollows()
  {
    var cut = Render();

    await cut.Instance.OnDragMove(0.7);
    await cut.Instance.OnDragCancel();
    await cut.Instance.OnDragEnd(0.7);

    Assert.Empty(_seeks);
    Assert.Equal([0.7, null], _scrubs);
  }

  [Fact]
  public async Task Disabled_IgnoresTheGesture_AndRendersNoThumb()
  {
    var cut = Render(disabled: true);

    await cut.Instance.OnDragMove(0.4);
    await cut.Instance.OnDragEnd(0.4);

    Assert.Empty(_seeks);
    Assert.Empty(_scrubs);
    Assert.Empty(cut.FindAll(".seek-bar-thumb"));
    Assert.Equal("true", cut.Find(".seek-bar").GetAttribute("aria-disabled"));
  }

  [Fact]
  public async Task OutOfRangeFractions_AreClamped()
  {
    var cut = Render();

    await cut.Instance.OnDragMove(-0.5);
    await cut.Instance.OnDragEnd(1.7);

    Assert.Equal([1.0], _seeks);
  }

  [Fact]
  public void FirstRender_AttachesTheGestureModule()
  {
    var module = JSInterop.SetupModule("./js/seek-bar.js");
    module.SetupVoid("attach", _ => true).SetVoidResult();

    Render();

    module.VerifyInvoke("attach");
  }
}
