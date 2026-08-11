using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Components.Shared;
using Radio.Web.Services.ApiClients;

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
public class NowPlayingPanelVolumeDebounceTests
{
  /// <summary>Comfortably past the panel's 300ms debounce window.</summary>
  private static readonly TimeSpan PastDebounceWindow = TimeSpan.FromMilliseconds(1500);

  /// <summary>
  /// Records every request so the test can count writes to the configuration endpoint.
  /// </summary>
  private sealed class RecordingHandler : HttpMessageHandler
  {
    private readonly List<(HttpMethod Method, string Path)> _requests = [];
    private readonly object _sync = new();

    public IReadOnlyList<(HttpMethod Method, string Path)> Requests
    {
      get
      {
        lock (_sync)
        {
          return _requests.ToList();
        }
      }
    }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      lock (_sync)
      {
        _requests.Add((request.Method, request.RequestUri?.AbsolutePath ?? string.Empty));
      }

      // "{}" satisfies both the PlaybackStateDto read on the volume call and the
      // dictionary read the configuration client performs before it writes.
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
      });
    }
  }

  private static (NowPlayingPanel Panel, RecordingHandler Handler) CreatePanel()
  {
    var handler = new RecordingHandler();

    var audioClient = new HttpClient(handler, disposeHandler: false)
    {
      BaseAddress = new Uri("http://localhost:5000")
    };
    var configClient = new HttpClient(handler, disposeHandler: false)
    {
      BaseAddress = new Uri("http://localhost:5000")
    };

    var panel = new NowPlayingPanel();
    SetInjected(panel, "AudioApi",
      new AudioApiService(audioClient, NullLogger<AudioApiService>.Instance));
    SetInjected(panel, "ConfigApi",
      new ConfigurationApiService(configClient, NullLogger<ConfigurationApiService>.Instance));
    SetInjected(panel, "Logger", NullLogger<NowPlayingPanel>.Instance);

    return (panel, handler);
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

  private static int CountConfigWrites(RecordingHandler handler) =>
    handler.Requests.Count(r => r.Method == HttpMethod.Post && r.Path == "/api/configuration/ui.playback");

  private static int CountVolumeCalls(RecordingHandler handler) =>
    handler.Requests.Count(r => r.Method == HttpMethod.Post && r.Path.StartsWith("/api/audio/volume/"));

  /// <summary>
  /// The headline regression: a 13-tick drag must persist once, not 13 times.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_PersistsThePreferenceOnce()
  {
    var (panel, handler) = CreatePanel();

    for (var i = 0; i < 13; i++)
    {
      await InvokeVolumeChange(panel, 40 + i);
    }

    await Task.Delay(PastDebounceWindow);

    Assert.Equal(1, CountConfigWrites(handler));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// The audible half of the slider must stay immediate — debouncing the volume itself
  /// would make the control feel broken. Every tick still reaches /api/audio/volume.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_StillAppliesEveryTickToTheAudioEngine()
  {
    var (panel, handler) = CreatePanel();

    for (var i = 0; i < 13; i++)
    {
      await InvokeVolumeChange(panel, 40 + i);
    }

    Assert.Equal(13, CountVolumeCalls(handler));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Coalescing must keep the value the user actually released on, not the first tick.
  /// </summary>
  [Fact]
  public async Task VolumeDrag_PersistsTheFinalValue()
  {
    var (panel, handler) = CreatePanel();

    await InvokeVolumeChange(panel, 10);
    await InvokeVolumeChange(panel, 55);
    await InvokeVolumeChange(panel, 88);

    await Task.Delay(PastDebounceWindow);

    var pending = typeof(NowPlayingPanel)
      .GetField("_pendingVolumePreference", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(panel);

    Assert.Equal(88d, pending);
    Assert.Equal(1, CountConfigWrites(handler));

    StopDebounceTimer(panel);
  }

  /// <summary>
  /// Two deliberate, separated adjustments are two user actions and must both persist.
  /// </summary>
  [Fact]
  public async Task SeparatedVolumeChanges_EachPersist()
  {
    var (panel, handler) = CreatePanel();

    await InvokeVolumeChange(panel, 20);
    await Task.Delay(PastDebounceWindow);
    await InvokeVolumeChange(panel, 70);
    await Task.Delay(PastDebounceWindow);

    Assert.Equal(2, CountConfigWrites(handler));

    StopDebounceTimer(panel);
  }
}
