using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.Web.Components.Shared;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// Clock-driven tests for the source-gain slider's 200 ms trailing-edge debounce.
///
/// <para>The gain slider has the same write-amplification exposure the volume slider had — a drag
/// emits a value-changed event per pixel and each one would otherwise reach
/// <c>/api/audio/sourcegain</c>. It was debounced from the start and, until TEST-7, never tested.
/// It is covered here rather than in <c>NowPlayingPanelVolumeDebounceTests</c> because it is a
/// different endpoint with a different hop count: one POST, where a volume preference write is a
/// GET followed by a POST.</para>
/// </summary>
/// <remarks>
/// Same discipline as its volume sibling: advance a <c>FakeTimeProvider</c>, then rendezvous on the
/// request itself. No test here waits on wall-clock time, and the "nothing written yet" assertions
/// are exact rather than bounded. See TEST-7 in <c>docs/queue/TEST-7.md</c>.
/// </remarks>
public class NowPlayingPanelGainDebounceTests
{
  private const string SourceType = "FilePlayer";

  private static (NowPlayingPanel Panel, RecordingHandler Handler, FakeTimeProvider Clock) CreatePanel()
  {
    var handler = new RecordingHandler();
    var clock = new FakeTimeProvider();

    var audioClient = new HttpClient(handler, disposeHandler: false)
    {
      BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl)
    };

    var panel = new NowPlayingPanel { Clock = clock };
    SetPrivate(panel, "AudioApi",
      new AudioApiService(audioClient, NullLogger<AudioApiService>.Instance), isProperty: true);
    SetPrivate(panel, "Logger", NullLogger<NowPlayingPanel>.Instance, isProperty: true);

    // OnGainSliderChanged returns immediately when _nowPlayingSourceType is empty (TEST-7 C-123),
    // so a test that skips this passes vacuously.
    SetPrivate(panel, "_nowPlayingSourceType", SourceType, isProperty: false);

    return (panel, handler, clock);
  }

  private static void SetPrivate(NowPlayingPanel panel, string name, object value, bool isProperty)
  {
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    if (isProperty)
    {
      var property = typeof(NowPlayingPanel).GetProperty(name, Flags);
      Assert.True(property is not null, $"NowPlayingPanel should inject a '{name}' service");
      property!.SetValue(panel, value);
      return;
    }

    var field = typeof(NowPlayingPanel).GetField(name, Flags);
    Assert.True(field is not null, $"NowPlayingPanel should hold a '{name}' field");
    field!.SetValue(panel, value);
  }

  /// <summary>Drives the slider handler. Synchronous by design — it arms a timer and returns.</summary>
  private static void InvokeGainChange(NowPlayingPanel panel, float gain)
  {
    var method = typeof(NowPlayingPanel).GetMethod(
      "OnGainSliderChanged", BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.True(method is not null, "NowPlayingPanel should expose OnGainSliderChanged");
    method!.Invoke(panel, [gain]);
  }

  private static void StopDebounceTimer(NowPlayingPanel panel)
  {
    var field = typeof(NowPlayingPanel).GetField(
      "_gainDebounceTimer", BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.True(field is not null, "NowPlayingPanel should debounce source-gain writes");
    (field!.GetValue(panel) as IDisposable)?.Dispose();
  }

  /// <summary>A 13-tick gain drag must reach the API once, not 13 times.</summary>
  [Fact]
  public async Task GainDrag_WritesOnceAfterTheWindow()
  {
    var (panel, handler, clock) = CreatePanel();

    for (var i = 0; i < 13; i++)
    {
      InvokeGainChange(panel, 0.5f + (i * 0.01f));
    }

    Assert.Equal(0, handler.Count(RecordingHandler.IsSourceGainCall));

    clock.Advance(NowPlayingPanel.SourceGainDebounce);
    await handler.WaitForAsync(RecordingHandler.IsSourceGainCall, 1);

    Assert.Equal(1, handler.Count(RecordingHandler.IsSourceGainCall));

    StopDebounceTimer(panel);
  }

  /// <summary>Nothing reaches the API before the window closes.</summary>
  [Fact]
  public void GainDrag_DoesNotWriteBeforeTheWindowElapses()
  {
    var (panel, handler, clock) = CreatePanel();

    InvokeGainChange(panel, 0.5f);
    clock.Advance(NowPlayingPanel.SourceGainDebounce - TimeSpan.FromMilliseconds(1));

    Assert.Equal(0, handler.Count(RecordingHandler.IsSourceGainCall));

    StopDebounceTimer(panel);
  }

  /// <summary>Every tick re-arms the window, which is what collapses a drag to one write.</summary>
  [Fact]
  public async Task GainDrag_ReArmsTheWindowOnEveryTick()
  {
    var (panel, handler, clock) = CreatePanel();
    var half = NowPlayingPanel.SourceGainDebounce / 2;   // 100 ms

    InvokeGainChange(panel, 0.5f);     // armed for t+200
    clock.Advance(half);               // t = 100
    InvokeGainChange(panel, 0.6f);     // re-armed for t+300
    clock.Advance(half);               // t = 200 — the ORIGINAL due time

    Assert.Equal(0, handler.Count(RecordingHandler.IsSourceGainCall));

    clock.Advance(half);               // t = 300
    await handler.WaitForAsync(RecordingHandler.IsSourceGainCall, 1);
    Assert.Equal(1, handler.Count(RecordingHandler.IsSourceGainCall));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Coalescing keeps the value the user released on.
  /// </summary>
  /// <remarks>
  /// Asserts on <c>_pendingGainValue</c>, not on the request path. <c>SetSourceGainAsync</c> builds
  /// its URL with <c>{gain:F2}</c>, which is <b>current-culture</b> formatting (TEST-7 C-126), so a
  /// path assertion would encode the runner's locale into the test. That defect is filed, not fixed
  /// here. <c>_pendingGainValue</c> is assigned synchronously inside <c>OnGainSliderChanged</c>, so
  /// this assertion is timing-independent for the same reason its volume twin is.
  /// </remarks>
  [Fact]
  public async Task GainDrag_WritesTheFinalValue()
  {
    var (panel, handler, clock) = CreatePanel();

    InvokeGainChange(panel, 0.50f);
    InvokeGainChange(panel, 0.75f);
    InvokeGainChange(panel, 0.25f);

    clock.Advance(NowPlayingPanel.SourceGainDebounce);
    await handler.WaitForAsync(RecordingHandler.IsSourceGainCall, 1);

    var pending = typeof(NowPlayingPanel)
      .GetField("_pendingGainValue", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(panel);

    Assert.Equal(0.25f, pending);
    Assert.Equal(1, handler.Count(RecordingHandler.IsSourceGainCall));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// The reset button is a slider event, not a second path — it must debounce identically.
  /// Pins the comment on <c>ResetGain</c> that says exactly this.
  /// </summary>
  [Fact]
  public async Task ResetGain_GoesThroughTheSameDebounce()
  {
    var (panel, handler, clock) = CreatePanel();

    var reset = typeof(NowPlayingPanel).GetMethod(
      "ResetGain", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.True(reset is not null, "NowPlayingPanel should expose ResetGain");
    reset!.Invoke(panel, []);

    Assert.Equal(0, handler.Count(RecordingHandler.IsSourceGainCall));

    clock.Advance(NowPlayingPanel.SourceGainDebounce);
    await handler.WaitForAsync(RecordingHandler.IsSourceGainCall, 1);

    var pending = typeof(NowPlayingPanel)
      .GetField("_pendingGainValue", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(panel);

    Assert.Equal(1.0f, pending);

    StopDebounceTimer(panel);
  }
}
