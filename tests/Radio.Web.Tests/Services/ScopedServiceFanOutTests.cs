using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The two scoped services that carry the same multicast-await defect as
/// <c>AudioStateHubService</c> (queue row `UI-7`, Task 2): <see cref="DeviceDisplayStateService"/> and
/// what was then <c>RadioPanelToggleService</c> — retired by `UI-17`, whose replacement
/// <see cref="CentrePanelViewService"/> keeps the same per-subscriber loop and is tested here in its
/// place.
/// </summary>
/// <remarks>
/// ⚠ THE ROW'S SEVERITIES ARE INVERTED AND THESE TWO ARE THE LATENT ONES (`UI-7` `C-206`). The row
/// listed three "singletons"; two of them — these — are actually <c>AddScoped</c>
/// (Program.cs:475/:476), so one instance per circuit with exactly one subscriber each
/// (MainLayout.razor:387 and Home.razor:36). Their invocation list is length 1 and the defect was
/// genuinely dormant here, which is the opposite of the hub the row called dormant.
///
/// ⛔ The third, <c>PhoneUnreadState</c>, is NOT this defect and is not tested here: its event is
/// <c>Action&lt;int&gt;</c>, void-returning, so <c>Invoke</c> really does run every handler and there
/// is no discarded Task (`C-205`). <c>ConsolePlaybackState.cs:44-46</c> already documented that
/// exclusion before this row existed.
///
/// ⚠ They are fixed anyway, and not for symmetry: the lint in <c>Radio.Core.Tests</c> is global and
/// keyed on the declaration, so it fails on these two whether or not anyone thinks they matter.
/// That makes them a dependency of Task 3, not a judgement call.
///
/// ⚠ No wall clocks — `CLAUDE.md` § *Test Timing*. The gate pattern is used, as in
/// <see cref="AudioStateHubServiceNotifyTests"/>.
/// </remarks>
public class ScopedServiceFanOutTests
{
  // --- CentrePanelViewService (UI-17; replaces RadioPanelToggleService) ------------------------

  /// <summary>
  /// Mutation `M4`, carried over from the service this one replaced: revert the loop to
  /// <c>await ViewRequested.Invoke(view)</c> and this assertion fails — in THIS file only. That the
  /// blast radius is one file is itself part of the assertion; the two services share no state and a
  /// cross-file failure would say they do.
  /// </summary>
  [Fact]
  public async Task CentrePanelViewRequestDoesNotCompleteWhileAnEarlierSubscriberIsStillRunning()
  {
    var service = new CentrePanelViewService(NullLogger<CentrePanelViewService>.Instance);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slowFinished = false;
    var fastRan = false;

    service.ViewRequested += async _ => { await gate.Task; slowFinished = true; };
    service.ViewRequested += _ => { fastRan = true; return Task.CompletedTask; };

    var notify = service.RequestViewAsync(CentrePanelView.Queue);

    Assert.False(notify.IsCompleted);
    Assert.False(fastRan);

    gate.SetResult();
    await notify;

    Assert.True(slowFinished);
    Assert.True(fastRan);
  }

  [Fact]
  public async Task CentrePanelViewSynchronousThrowDoesNotStarveLaterSubscribers()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var service = new CentrePanelViewService(new CapturingLogger<CentrePanelViewService>(sink));
    var third = false;

    service.ViewRequested += _ => Task.CompletedTask;
    service.ViewRequested += _ => throw new InvalidOperationException("synchronous throw");
    service.ViewRequested += _ => { third = true; return Task.CompletedTask; };

    await service.RequestViewAsync(CentrePanelView.Radio);

    Assert.True(third, "a synchronously throwing subscriber must not starve the ones after it");
    Assert.Contains(sink, e => e.Level == LogLevel.Warning);
  }

  /// <summary>
  /// With no panel mounted (the request came from another page) the request waits in
  /// <see cref="CentrePanelViewService.PendingView"/>, and collecting it clears it so it is applied
  /// at most once.
  /// </summary>
  [Fact]
  public async Task CentrePanelViewWithNoSubscribersHoldsTheRequestUntilTaken()
  {
    var service = new CentrePanelViewService(NullLogger<CentrePanelViewService>.Instance);

    await service.RequestViewAsync(CentrePanelView.Queue);

    Assert.Equal(CentrePanelView.Queue, service.PendingView);
    Assert.Equal(CentrePanelView.Queue, service.TakePendingView());
    Assert.Null(service.PendingView);
    Assert.Null(service.TakePendingView());
  }

  /// <summary>
  /// Not change-gated, unlike the toggle it replaced: two Queue-pill taps are two requests, because
  /// the owner may have picked another tab in between.
  /// </summary>
  [Fact]
  public async Task CentrePanelViewRaisesOnEveryRequestEvenARepeat()
  {
    var service = new CentrePanelViewService(NullLogger<CentrePanelViewService>.Instance);
    var raised = 0;
    service.ViewRequested += _ => { raised++; return Task.CompletedTask; };

    await service.RequestViewAsync(CentrePanelView.Queue);
    await service.RequestViewAsync(CentrePanelView.Queue);

    Assert.Equal(2, raised);
  }

  // --- DeviceDisplayStateService ---------------------------------------------------------------

  [Fact]
  public async Task DisplaySettingsChangedDoesNotCompleteWhileAnEarlierSubscriberIsStillRunning()
  {
    var service = new DeviceDisplayStateService(NullLogger<DeviceDisplayStateService>.Instance);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slowFinished = false;
    var fastRan = false;

    service.DisplaySettingsChanged += async () => { await gate.Task; slowFinished = true; };
    service.DisplaySettingsChanged += () => { fastRan = true; return Task.CompletedTask; };

    var notify = service.NotifyDisplaySettingsChangedAsync();

    Assert.False(notify.IsCompleted);
    Assert.False(fastRan);

    gate.SetResult();
    await notify;

    Assert.True(slowFinished);
    Assert.True(fastRan);
  }

  [Fact]
  public async Task DisplaySettingsChangedSynchronousThrowDoesNotStarveLaterSubscribers()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var service = new DeviceDisplayStateService(new CapturingLogger<DeviceDisplayStateService>(sink));
    var third = false;

    service.DisplaySettingsChanged += () => Task.CompletedTask;
    service.DisplaySettingsChanged += () => throw new InvalidOperationException("synchronous throw");
    service.DisplaySettingsChanged += () => { third = true; return Task.CompletedTask; };

    await service.NotifyDisplaySettingsChangedAsync();

    Assert.True(third, "a synchronously throwing subscriber must not starve the ones after it");
    Assert.Contains(sink, e => e.Level == LogLevel.Warning);
  }

  [Fact]
  public async Task DisplaySettingsChangedWithNoSubscribersDoesNotThrow()
  {
    var service = new DeviceDisplayStateService(NullLogger<DeviceDisplayStateService>.Instance);

    await service.NotifyDisplaySettingsChangedAsync();
  }
}
