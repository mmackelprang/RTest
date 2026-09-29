using Microsoft.Extensions.Logging;
using Radio.Core.Models.Audio;
using Radio.Metrics;

namespace Radio.Fingerprinting.Services;

/// <summary>
/// What one fingerprint capture window produced, as far as <see cref="FingerprintCaptureWatchdog"/> is
/// concerned.
/// </summary>
public enum CaptureWindowOutcome
{
  /// <summary>The window captured at least one chunk of non-zero audio (silent or not by RMS).</summary>
  Audio,

  /// <summary>
  /// The window captured zero audio bytes, and the capturer judged that audio should have been reaching it
  /// at both the start and the end of the window (the same source Playing, a lookup wanted, its pipeline up).
  /// A pause that begins and ends inside one window is not visible at either end.
  /// </summary>
  EmptyWhilePlaying,

  /// <summary>
  /// The window captured zero audio bytes, but for at least part of it audio had no reason to arrive —
  /// the source paused, stopped or changed, the capture was cancelled, or its pipeline was not up.
  /// </summary>
  EmptyNotPlaying,
}

/// <summary>
/// AUD-18: notices a sustained run of fingerprint capture windows that returned zero audio bytes while the
/// active source was playing, reports it once, and then stays quiet until a window carries audio again.
/// </summary>
/// <remarks>
/// <para>
/// On 2026-09-08 the fingerprint tap returned zero bytes for every 15 s window for 11 h 23 m — 240 failures
/// an hour, each logged twice, 18.6 % of the log — and nobody noticed. This class is the detection half of
/// that row only. <b>It does not restart anything and does not attempt a fix</b>; the root cause stays open
/// (docs/queue/AUD-18.md).
/// </para>
/// <para>
/// State machine, driven by <see cref="RecordWindow"/>:
/// <list type="bullet">
/// <item><see cref="CaptureWindowOutcome.EmptyWhilePlaying"/> extends the run. When the run reaches
/// <see cref="TripAfterConsecutiveEmptyWindows"/> the watchdog latches: one Warning, one increment of
/// <c>fingerprint.capture_starvation_trips</c>.</item>
/// <item><see cref="CaptureWindowOutcome.EmptyNotPlaying"/> ends an un-latched run (a gap in playback means
/// the run was not sustained). While latched it changes nothing — only audio ends an outage.</item>
/// <item><see cref="CaptureWindowOutcome.Audio"/> ends the run; if latched, one Information line with the
/// outage duration, and the latch clears.</item>
/// </list>
/// While <see cref="IsLatched"/> is true the per-window "no audio" Warnings in <c>SoundFlowAudioTap</c> and
/// <see cref="BackgroundIdentificationService"/> are logged at Debug instead. The windows before the trip
/// keep their Warnings.
/// </para>
/// <para>
/// The logger category is deliberately under <c>Radio.Fingerprinting</c>, which the shipped
/// <c>appsettings.json</c> sets to Information: <c>Radio.Infrastructure.Audio</c> (where the tap lives) is held
/// at Warning by <c>LOG-2</c>, so a recovery line logged from there would reach neither sink by default.
/// </para>
/// <para>
/// Captures are serial (one identification loop), but <see cref="IsLatched"/> may be read from another
/// thread, so all state is guarded by one lock.
/// </para>
/// </remarks>
public sealed class FingerprintCaptureWatchdog
{
  /// <summary>
  /// Consecutive <see cref="CaptureWindowOutcome.EmptyWhilePlaying"/> windows that trip the watchdog.
  /// </summary>
  /// <remarks>
  /// 20 windows is five minutes at the default 15 s <c>SampleDurationSeconds</c>. Chosen from the box's own
  /// record after the 2026-09-08 outage (radio-api file sink, 2026-09-23 → 09-29): the longest back-to-back
  /// runs of <c>No audio data captured</c> were 3 (09-29, the start of a Bluetooth session), 5 (09-26, BT
  /// selected and Playing before its capture device existed), 13 (09-25 11:30, minutes after a BT session
  /// started) and 23 (09-25 23:48, BT Playing with AVRCP tracks advancing and nothing reaching
  /// the tap for six minutes). 20 clears every run that has an innocent explanation with margin; the 23-window
  /// run would trip, and one Warning for six minutes of zero-byte capture during reported playback is the
  /// intended outcome, not a false alarm. Paused sources never count at all — they are not Playing, so the
  /// capture is not attempted — and a stretch of quiet music is not empty either: any non-zero chunk makes the
  /// window <see cref="CaptureWindowOutcome.Audio"/>. The 2026-09-08 outage would have tripped five minutes in
  /// instead of running unnoticed for eleven hours.
  /// </remarks>
  public const int TripAfterConsecutiveEmptyWindows = 20;

  private readonly ILogger<FingerprintCaptureWatchdog> _logger;
  private readonly IMetricsCollector? _metricsCollector;
  private readonly TimeProvider _timeProvider;
  private readonly object _lock = new();

  private int _consecutiveEmptyWindows;
  private DateTimeOffset? _runStartedAt;
  private bool _latched;

  /// <summary>
  /// Initializes a new instance of the <see cref="FingerprintCaptureWatchdog"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="metricsCollector">Optional metrics collector.</param>
  /// <param name="timeProvider">Clock for outage durations; defaults to <see cref="TimeProvider.System"/>.</param>
  public FingerprintCaptureWatchdog(
    ILogger<FingerprintCaptureWatchdog> logger,
    IMetricsCollector? metricsCollector = null,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _metricsCollector = metricsCollector;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <summary>
  /// True from the window that tripped the watchdog until the next window that captured audio.
  /// </summary>
  public bool IsLatched
  {
    get
    {
      lock (_lock)
      {
        return _latched;
      }
    }
  }

  /// <summary>
  /// Records the outcome of one capture window. Call once per window that was actually attempted.
  /// </summary>
  /// <param name="outcome">What the window produced.</param>
  /// <param name="sourceName">The active source's display name, for the log lines.</param>
  /// <param name="sourceType">The active source's type, for the metric tag.</param>
  /// <param name="windowElapsed">How long the window ran; the first empty window's start is the outage start.</param>
  public void RecordWindow(
    CaptureWindowOutcome outcome, string sourceName, PlaySource sourceType, TimeSpan windowElapsed)
  {
    lock (_lock)
    {
      var now = _timeProvider.GetUtcNow();
      switch (outcome)
      {
        case CaptureWindowOutcome.Audio:
          if (_latched)
          {
            var outage = _runStartedAt is { } started ? now - started : TimeSpan.Zero;
            _logger.LogInformation(
              "Fingerprint capture recovered on {Source}: audio reached the tap again after {Outage} " +
              "({Windows} empty capture windows while playing)",
              sourceName, FormatDuration(outage), _consecutiveEmptyWindows);
            _latched = false;
          }
          _consecutiveEmptyWindows = 0;
          _runStartedAt = null;
          break;

        case CaptureWindowOutcome.EmptyWhilePlaying:
          if (_consecutiveEmptyWindows == 0)
          {
            _runStartedAt = now - windowElapsed;
          }
          _consecutiveEmptyWindows++;
          if (!_latched && _consecutiveEmptyWindows >= TripAfterConsecutiveEmptyWindows)
          {
            _latched = true;
            var run = _runStartedAt is { } runStart ? now - runStart : windowElapsed;
            _logger.LogWarning(
              "Fingerprint capture has returned zero audio bytes for {Windows} consecutive windows ({Duration}) " +
              "while {Source} was playing; song identification has no audio to work with. Per-window " +
              "'no audio' warnings are logged at Debug until audio returns",
              _consecutiveEmptyWindows, FormatDuration(run), sourceName);
            _metricsCollector?.Increment("fingerprint.capture_starvation_trips", 1,
              new Dictionary<string, string> { ["source"] = sourceType.ToString().ToLowerInvariant() });
          }
          break;

        case CaptureWindowOutcome.EmptyNotPlaying:
          // Only audio ends a latched outage; an un-latched run is broken by any gap in playback.
          if (!_latched)
          {
            _consecutiveEmptyWindows = 0;
            _runStartedAt = null;
          }
          break;
      }
    }
  }

  private static string FormatDuration(TimeSpan span) =>
    span.TotalHours >= 1
      ? $"{(int)span.TotalHours}h {span.Minutes}m"
      : span.TotalMinutes >= 1
        ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
        : $"{span.TotalSeconds:F0}s";
}
