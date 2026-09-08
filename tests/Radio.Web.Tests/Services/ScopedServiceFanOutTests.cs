using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The two scoped services that carry the same multicast-await defect as
/// <c>AudioStateHubService</c> (queue row `UI-7`, Task 2): <see cref="RadioPanelToggleService"/> and
/// <see cref="DeviceDisplayStateService"/>.
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
  // --- RadioPanelToggleService -----------------------------------------------------------------

  /// <summary>
  /// Mutation `M4`: revert this service's loop to <c>await RadioPanelToggled.Invoke()</c> and this
  /// assertion fails — in THIS file only. That the blast radius is one file is itself part of the
  /// assertion; the two services share no state and a cross-file failure would say they do.
  /// </summary>
  [Fact]
  public async Task RadioPanelToggleDoesNotCompleteWhileAnEarlierSubscriberIsStillRunning()
  {
    var service = new RadioPanelToggleService(NullLogger<RadioPanelToggleService>.Instance);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slowFinished = false;
    var fastRan = false;

    service.RadioPanelToggled += async () => { await gate.Task; slowFinished = true; };
    service.RadioPanelToggled += () => { fastRan = true; return Task.CompletedTask; };

    var notify = service.ShowRadioPanelAsync();

    Assert.False(notify.IsCompleted);
    Assert.False(fastRan);

    gate.SetResult();
    await notify;

    Assert.True(slowFinished);
    Assert.True(fastRan);
  }

  [Fact]
  public async Task RadioPanelToggleSynchronousThrowDoesNotStarveLaterSubscribers()
  {
    var sink = new List<(LogLevel Level, string Message)>();
    var service = new RadioPanelToggleService(new CapturingLogger<RadioPanelToggleService>(sink));
    var third = false;

    service.RadioPanelToggled += () => Task.CompletedTask;
    service.RadioPanelToggled += () => throw new InvalidOperationException("synchronous throw");
    service.RadioPanelToggled += () => { third = true; return Task.CompletedTask; };

    await service.ShowRadioPanelAsync();

    Assert.True(third, "a synchronously throwing subscriber must not starve the ones after it");
    Assert.Contains(sink, e => e.Level == LogLevel.Warning);
  }

  [Fact]
  public async Task RadioPanelToggleWithNoSubscribersDoesNotThrow()
  {
    var service = new RadioPanelToggleService(NullLogger<RadioPanelToggleService>.Instance);

    await service.ShowRadioPanelAsync();

    Assert.True(service.IsRadioPanelVisible);
  }

  /// <summary>
  /// The idempotent-set contract predates this row and must survive it: setting the same value
  /// raises nothing at all.
  /// </summary>
  [Fact]
  public async Task RadioPanelToggleDoesNotRaiseOnAnIdempotentSet()
  {
    var service = new RadioPanelToggleService(NullLogger<RadioPanelToggleService>.Instance);
    var raised = 0;

    await service.ShowRadioPanelAsync();
    service.RadioPanelToggled += () => { raised++; return Task.CompletedTask; };
    await service.ShowRadioPanelAsync();

    Assert.Equal(0, raised);
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
