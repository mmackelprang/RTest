using FluentAssertions;
using Radio.Web.Services;
using Xunit;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Unit tests for <see cref="GainPopoverService"/> — Task #15 PR E item #47.
///
/// The service portals the gain-popover click-away backdrop from inside
/// <c>NowPlayingPanel.razor</c> (which sits under <c>.page-transition</c> and
/// gets trapped by that wrapper's stacking context) up to <c>MainLayout</c>
/// where it can render above any descendant. These tests pin the four
/// load-bearing invariants:
///
/// 1. Open/Close flip <see cref="GainPopoverService.IsOpen"/>.
/// 2. <see cref="GainPopoverService.StateChanged"/> fires only on real
///    transitions (idempotent set + event-on-change, mirrors
///    <c>RadioPanelToggleService</c>).
/// 3. <see cref="GainPopoverService.HandleBackdropClick"/> invokes the
///    <see cref="GainPopoverService.OnClose"/> subscribers AND closes.
/// 4. The OnClose subscribers are fired even when one throws — Close()
///    runs in a finally so the backdrop never gets stuck open.
/// </summary>
public class GainPopoverServiceTests
{
  [Fact]
  public void Open_SetsIsOpenTrue_AndFiresStateChanged()
  {
    var svc = new GainPopoverService();
    var fires = 0;
    svc.StateChanged += () => fires++;

    svc.Open();

    svc.IsOpen.Should().BeTrue();
    fires.Should().Be(1);
  }

  [Fact]
  public void Close_SetsIsOpenFalse_AndFiresStateChanged()
  {
    var svc = new GainPopoverService();
    svc.Open();
    var fires = 0;
    svc.StateChanged += () => fires++;

    svc.Close();

    svc.IsOpen.Should().BeFalse();
    fires.Should().Be(1);
  }

  [Fact]
  public void Open_TwiceInARow_FiresStateChangedOnlyOnce()
  {
    // Idempotent set + event-on-change — subscribers should only see real
    // transitions, never a redundant Open() while already open.
    var svc = new GainPopoverService();
    var fires = 0;
    svc.StateChanged += () => fires++;

    svc.Open();
    svc.Open();
    svc.Open();

    svc.IsOpen.Should().BeTrue();
    fires.Should().Be(1);
  }

  [Fact]
  public void Close_WhenAlreadyClosed_DoesNotFireStateChanged()
  {
    var svc = new GainPopoverService();
    var fires = 0;
    svc.StateChanged += () => fires++;

    svc.Close();        // already closed
    svc.Close();
    svc.Close();

    svc.IsOpen.Should().BeFalse();
    fires.Should().Be(0);
  }

  [Fact]
  public void HandleBackdropClick_FiresOnClose_AndClosesPopover()
  {
    // Wire-path: the layout-mounted backdrop's @onclick → HandleBackdropClick
    // → OnClose subscribers (NowPlayingPanel tears down local state) → Close()
    // → backdrop unmounts via StateChanged in MainLayout.
    var svc = new GainPopoverService();
    svc.Open();

    var onCloseFires = 0;
    var stateChangedFires = 0;
    svc.OnClose += () => onCloseFires++;
    svc.StateChanged += () => stateChangedFires++;

    svc.HandleBackdropClick();

    onCloseFires.Should().Be(1,
      "the panel must be notified so it can tear down its own popover state");
    svc.IsOpen.Should().BeFalse(
      "the backdrop must unmount itself once the click is handled");
    stateChangedFires.Should().Be(1,
      "MainLayout must re-render to remove the backdrop element");
  }

  [Fact]
  public void HandleBackdropClick_OnCloseSubscriberThrows_StillClosesPopover()
  {
    // Robustness: if a subscriber blows up, the backdrop must still close so
    // we don't leak a fullscreen click-blocker over the kiosk UI.
    var svc = new GainPopoverService();
    svc.Open();
    svc.OnClose += () => throw new InvalidOperationException("subscriber boom");

    var act = () => svc.HandleBackdropClick();

    // The exception propagates (we want it surfaced to the logging pipeline),
    // but Close() runs first in a finally block.
    act.Should().Throw<InvalidOperationException>();
    svc.IsOpen.Should().BeFalse();
  }
}
