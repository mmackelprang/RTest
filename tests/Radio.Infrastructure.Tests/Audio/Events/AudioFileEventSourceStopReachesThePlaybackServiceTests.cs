using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.Audio.SoundFlow;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.Audio.Events;

/// <summary>
/// <c>PHN-10</c>: <c>AudioFileEventSource</c>'s stop and dispose paths reach
/// <c>SoundFlowPlaybackService.StopAsync</c> whatever <c>_isPlaybackActive</c> says.
/// </summary>
/// <remarks>
/// ⭐ <b>WHAT THIS PROVES, exactly.</b> That the call is ATTEMPTED unconditionally. Nothing more.
/// It does not prove a <c>SoundPlayer</c> is stopped, detached from the mixer or disposed — that
/// needs a real MiniAudio device and is owner UAT (plan §3 U1/U3/U5). Read a green run as "the stop
/// path calls the stopping code", not as "voicemails stop".
///
/// ⭐ <b>HOW it observes the call, and why that particular trick.</b> There is no seam to watch.
/// <c>AudioFileEventSource</c> takes the CONCRETE <c>SoundFlowPlaybackService</c>, whose
/// <c>StopAsync</c> is neither virtual nor behind an interface, so it cannot be mocked; and a
/// device-free service registers no player, so a real <c>StopAsync</c> on it is a silent no-op with
/// nothing to assert on (<c>DeviceFreePlaybackService</c> carries that reasoning). The one
/// observable difference between "called" and "not called" is
/// <c>SoundFlowPlaybackService.StopAsync</c>'s opening <c>ThrowIfDisposed()</c>: against a DISPOSED
/// service the call throws <c>ObjectDisposedException</c>, PHN-10's new <c>catch</c> logs it, and
/// the log line is the evidence that control got there.
///
/// ⚠ <b>So the disposed service is an INSTRUMENT, not the scenario.</b> Nothing in production
/// disposes the playback service under a live source. If a future change removes
/// <c>ThrowIfDisposed</c> from <c>StopAsync</c>, or stops wrapping the call in
/// <c>AudioFileEventSource</c>, these tests go RED rather than quietly stopping proving anything —
/// which is the property that makes the trick acceptable.
///
/// ⛔ <b>All three tests FAIL on the pre-PHN-10 source</b>, and that is the point of writing them:
/// with the <c>_isPlaybackActive</c> guard in place, <c>_playbackService.StopAsync</c> is never
/// called on a source that never reached <c>PlayWithSoundFlowAsync</c>'s success path — so no
/// exception, no warning, no log line. <b>Measured, not assumed</b>: the two stop guards were
/// temporarily reinstated on this branch and all three went red (3 failed / 0 passed), then green
/// again (3 passed) when they were removed.
/// </remarks>
public class AudioFileEventSourceStopReachesThePlaybackServiceTests
{
  private const string StopWarning = "Error stopping audio file event playback through SoundFlow";
  private const string DisposeWarning = "Error stopping audio file event playback during disposal";

  [Fact]
  public async Task StopAsync_CallsThePlaybackService_EvenThoughPlaybackWasNeverActive()
  {
    var (logs, source) = CreateInitializedSourceOverADisposedService();

    await source.StopAsync();

    Assert.Contains(logs.Messages, m => m.Contains(StopWarning, StringComparison.Ordinal));
  }

  [Fact]
  public async Task DisposeAsync_CallsThePlaybackService_EvenThoughPlaybackWasNeverActive()
  {
    var (logs, source) = CreateInitializedSourceOverADisposedService();

    await source.DisposeAsync();

    Assert.Contains(logs.Messages, m => m.Contains(DisposeWarning, StringComparison.Ordinal));
  }

  [Fact]
  public async Task TheTwoStopPathsAreDistinguishable()
  {
    // ⚠ Guards the assertions above against each other. Both call the same service method, so a
    // single shared catch — or a copy-pasted message — would let one test pass on the other's
    // evidence. StopCoreAsync and DisposeAsyncCore are separate backstops (the dispose arm is the
    // one the voicemail path never had), so their evidence has to be separable.
    var (logs, source) = CreateInitializedSourceOverADisposedService();

    await source.StopAsync();

    Assert.Contains(logs.Messages, m => m.Contains(StopWarning, StringComparison.Ordinal));
    Assert.DoesNotContain(logs.Messages, m => m.Contains(DisposeWarning, StringComparison.Ordinal));
  }

  /// <summary>
  /// An initialised source holding a disposed playback service. Initialisation is what mints
  /// <c>_playbackId</c>, without which the (correct, retained) null guard would skip the call for a
  /// reason that has nothing to do with PHN-10.
  /// </summary>
  /// <remarks>
  /// ⚠ The stream constructor deliberately, so no file has to exist. Playback is never started: the
  /// whole point is that <c>_isPlaybackActive</c> is false — which is the state every real stop path
  /// arrived in, because <c>EventPlaybackService.TearDownAsync</c> cancels the playback token before
  /// it stops the source and the cancellation handler clears the flag.
  /// </remarks>
  private static (CapturingLoggerProvider Logs, AudioFileEventSource Source)
    CreateInitializedSourceOverADisposedService()
  {
    var logs = new CapturingLoggerProvider();
    var playbackService = DeviceFreePlaybackService.Create();
    playbackService.Dispose();

    var source = new AudioFileEventSource(
      "PHN-10",
      new MemoryStream(new byte[1000]),
      TimeSpan.FromSeconds(1),
      logs.CreateLogger<AudioFileEventSource>(),
      playbackService);

    source.InitializeAsync().GetAwaiter().GetResult();

    return (logs, source);
  }
}
