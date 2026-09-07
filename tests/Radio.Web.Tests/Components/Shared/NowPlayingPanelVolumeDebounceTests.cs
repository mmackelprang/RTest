using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Web.Components.Shared;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// Regression tests for the volume slider's write amplification.
///
/// <para>A drag emits one value-changed event per pixel. The panel used to persist
/// <c>ui.playback</c> on every one of them — the journal from 2026-08-10 shows 13 config
/// writes inside a single second. On the API side each write reloads
/// <c>IOptionsMonitor&lt;AudioOutputOptions&gt;</c>, and each reload re-enumerates audio
/// devices, so one drag put a dozen threads into MiniAudio's non-thread-safe PulseAudio main
/// loop and aborted <c>radio-api</c> with SIGABRT.</para>
///
/// <para>These tests drive the panel's handler directly rather than rendering it: the
/// behaviour under test is the debounce timer and the API traffic it produces, and a bare
/// instance keeps the test free of the Radzen/SignalR scaffolding a full render needs.</para>
/// </summary>
/// <remarks>
/// <para>
/// These tests are <b>clock-driven, not sleep-driven</b> (TEST-7). Every one of them advances a
/// <c>FakeTimeProvider</c> the panel was built with and then rendezvouses on the request the
/// debounce callback actually makes. Nothing here waits on wall-clock time.
/// </para>
/// <para>
/// The shape they replace raced <c>await Task.Delay(1500)</c> against the panel's own 300 ms timer
/// with no rendezvous, and could fail in <b>both</b> directions — undershoot to zero writes if the
/// callback and its two HTTP hops missed the window, overshoot to two if a stall inserted more than
/// 300 ms between the un-slept setup invokes. Both are now closed, and by different mechanisms: the
/// fake clock makes an extra callback impossible, and <c>WaitForAsync</c> makes a missing one
/// impossible. Same defect as TEST-4, one layer out.
/// </para>
/// <para>
/// So a "nothing has been written yet" assertion below is <b>exact</b>, not a bounded negative: no
/// timer is due, so none can fire, however loaded the machine is. <c>RendezvousTimeout</c> is a
/// deadlock guard and is not reached on a passing run.
/// </para>
/// </remarks>
public class NowPlayingPanelVolumeDebounceTests
{
  private static (NowPlayingPanel Panel, RecordingHandler Handler, FakeTimeProvider Clock) CreatePanel()
  {
    var handler = new RecordingHandler();
    var clock = new FakeTimeProvider();

    var audioClient = new HttpClient(handler, disposeHandler: false)
    {
      BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl)
    };
    var configClient = new HttpClient(handler, disposeHandler: false)
    {
      BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl)
    };

    // Clock is set through the internal seam rather than by reflection: Radio.Web.csproj already
    // grants this assembly InternalsVisibleTo, and a compile-time set breaks loudly if the seam is
    // ever renamed, where a reflective one would silently start testing the system clock again.
    var panel = new NowPlayingPanel { Clock = clock };
    SetInjected(panel, "AudioApi",
      new AudioApiService(audioClient, NullLogger<AudioApiService>.Instance));
    SetInjected(panel, "ConfigApi",
      new ConfigurationApiService(configClient, NullLogger<ConfigurationApiService>.Instance));
    SetInjected(panel, "Logger", NullLogger<NowPlayingPanel>.Instance);

    return (panel, handler, clock);
  }

  private static void SetInjected(NowPlayingPanel panel, string propertyName, object value)
  {
    var property = typeof(NowPlayingPanel).GetProperty(
      propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    Assert.True(property is not null, $"NowPlayingPanel should inject a '{propertyName}' service");
    property!.SetValue(panel, value);
  }

  private static Task InvokeVolumeChange(NowPlayingPanel panel, double volume)
  {
    var method = typeof(NowPlayingPanel).GetMethod(
      "HandleVolumeChangeAsync", BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.True(method is not null, "NowPlayingPanel should expose HandleVolumeChangeAsync");
    return (Task)method!.Invoke(panel, [volume])!;
  }

  /// <summary>
  /// Releases the debounce timer. The panel's own <c>DisposeAsync</c> unsubscribes from the
  /// SignalR hub service, which these bare instances do not have, so the timer is released
  /// directly instead.
  /// </summary>
  private static void StopDebounceTimer(NowPlayingPanel panel)
  {
    var field = typeof(NowPlayingPanel).GetField(
      "_volumePrefDebounceTimer", BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.True(field is not null, "NowPlayingPanel should debounce volume-preference writes");
    (field!.GetValue(panel) as IDisposable)?.Dispose();
  }

  /// <summary>
  /// The headline regression: a 13-tick drag must persist once, not 13 times.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_PersistsThePreferenceOnce()
  {
    var (panel, handler, clock) = CreatePanel();

    for (var i = 0; i < 13; i++)
    {
      await InvokeVolumeChange(panel, 40 + i);
    }

    // Exact, not bounded: the clock has not advanced, so no callback can have run.
    Assert.Equal(0, handler.Count(RecordingHandler.IsConfigWrite));

    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce);
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 1);

    // Still exact: the timer is one-shot (InfiniteTimeSpan period) and the clock will not move
    // again, so no second write can appear after this line.
    Assert.Equal(1, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// The audible half of the slider must stay immediate — debouncing the volume itself would make
  /// the control feel broken. Every tick still reaches /api/audio/volume.
  /// </summary>
  /// <remarks>
  /// The one test in this file that never needed the seam: it performs no wait at all and asserts
  /// on calls the test itself awaited.
  /// </remarks>
  [Fact]
  public async Task VolumeDrag_StillAppliesEveryTickToTheAudioEngine()
  {
    var (panel, handler, _) = CreatePanel();

    for (var i = 0; i < 13; i++)
    {
      await InvokeVolumeChange(panel, 40 + i);
    }

    Assert.Equal(13, handler.Count(RecordingHandler.IsVolumeCall));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Coalescing must keep the value the user actually released on, not the first tick.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_PersistsTheFinalValue()
  {
    var (panel, handler, clock) = CreatePanel();

    await InvokeVolumeChange(panel, 10);
    await InvokeVolumeChange(panel, 55);
    await InvokeVolumeChange(panel, 88);

    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce);
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 1);

    var pending = typeof(NowPlayingPanel)
      .GetField("_pendingVolumePreference", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(panel);

    // Kept deliberately (TEST-7 C-128): _pendingVolumePreference is assigned synchronously inside
    // QueueVolumePreferenceSave, so this assertion never depended on timing — and it is this
    // file's only coverage of *which* value coalescing keeps.
    Assert.Equal(88d, pending);
    Assert.Equal(1, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Two deliberate, separated adjustments are two user actions and must both persist.
  /// </summary>
  [Fact]
  public async Task SeparatedVolumeChanges_EachPersist()
  {
    var (panel, handler, clock) = CreatePanel();

    await InvokeVolumeChange(panel, 20);
    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce);
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 1);

    await InvokeVolumeChange(panel, 70);
    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce);
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 2);

    Assert.Equal(2, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Nothing is persisted before the window closes. New with TEST-7 — impossible to state exactly
  /// against a wall clock, where "not yet" is only ever "not yet on this machine".
  /// </summary>
  [Fact]
  public async Task VolumeDrag_DoesNotPersistBeforeTheWindowElapses()
  {
    var (panel, handler, clock) = CreatePanel();

    await InvokeVolumeChange(panel, 40);
    clock.Advance(NowPlayingPanel.VolumePreferenceDebounce - TimeSpan.FromMilliseconds(1));

    Assert.Equal(0, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Every tick re-arms the window — the trailing edge is what makes a 13-tick drag one write.
  /// New with TEST-7, and the reason the seam is worth its production diff: this is the behaviour
  /// <c>QueueVolumePreferenceSave</c>'s <c>Change()</c> branch exists for, and nothing tested it.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_ReArmsTheWindowOnEveryTick()
  {
    var (panel, handler, clock) = CreatePanel();
    var half = NowPlayingPanel.VolumePreferenceDebounce / 2;   // 150 ms

    await InvokeVolumeChange(panel, 40);      // armed for t+300
    clock.Advance(half);                      // t = 150
    await InvokeVolumeChange(panel, 50);      // re-armed for t+450
    clock.Advance(half);                      // t = 300 — the ORIGINAL due time

    // Exact: 1 here would mean Change() did not re-arm and the first arming survived.
    Assert.Equal(0, handler.Count(RecordingHandler.IsConfigWrite));

    clock.Advance(half);                      // t = 450 — the re-armed due time
    await handler.WaitForAsync(RecordingHandler.IsConfigWrite, 1);
    Assert.Equal(1, handler.Count(RecordingHandler.IsConfigWrite));

    StopDebounceTimer(panel);
  }
}
