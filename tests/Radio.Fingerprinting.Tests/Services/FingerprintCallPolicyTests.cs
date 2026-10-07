using Microsoft.Extensions.Logging;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting.Services;

namespace Radio.Fingerprinting.Tests.Services;

/// <summary>
/// The owner's SongRec call policy (2026-10-07), driven directly. The policy holds no clock: every call
/// takes an explicit instant, so these tests step a simulated clock in whole seconds and nothing here
/// waits on, or races, a real timer.
/// </summary>
/// <remarks>
/// <see cref="Simulate"/> plays the identification loop's part: ask every simulated second; on
/// <see cref="CallDecisionKind.CallNow"/> "capture" for <c>captureLength</c>, record the call and its
/// outcome, and resume asking after the capture. Call times reported are capture starts — the anchor the
/// policy's start-to-start intervals are measured from.
/// </remarks>
public class FingerprintCallPolicyTests
{
  private static readonly DateTimeOffset T0 = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
  private static readonly TimeSpan Capture = TimeSpan.FromSeconds(13); // the default SampleDurationSeconds

  private readonly List<(LogLevel Level, string Message)> _logs = new();

  private FingerprintCallPolicy Create(FingerprintingOptions? options = null)
  {
    var o = options ?? new FingerprintingOptions();
    return new FingerprintCallPolicy(() => o, new ListLogger(_logs));
  }

  /// <summary>A source whose state the outcome callback can change, as the real sources do.</summary>
  private sealed class FakeSource(PlaySource type, string name)
  {
    public PlaySource Type { get; set; } = type;
    public string Name { get; set; } = name;
    public DateTime? TrackStartedUtc { get; set; }
    public bool NeedsLookup { get; set; } = true;
    public SourceSnapshot Snapshot => new(Type, Name, TrackStartedUtc, NeedsLookup);
  }

  private static FakeSource Radio() => new(PlaySource.Radio, "SDR Radio");

  private static FakeSource FileTrack(DateTimeOffset startedAt, bool needsLookup) =>
    new(PlaySource.File, "File Player") { TrackStartedUtc = startedAt.UtcDateTime, NeedsLookup = needsLookup };

  /// <summary>Runs the loop's side of the protocol from <paramref name="from"/> for <paramref name="duration"/>.</summary>
  /// <param name="policy">The policy under test.</param>
  /// <param name="source">The active source; callbacks may change it.</param>
  /// <param name="outcomeFor">The outcome of the n-th call (0-based), given its capture start.</param>
  /// <param name="duration">How much simulated time to run.</param>
  /// <param name="captureLength">How long each capture takes (default 13 s).</param>
  /// <param name="onSecond">Called before each decision, to change the source over time.</param>
  /// <param name="from">Simulated start (default <see cref="T0"/>).</param>
  /// <param name="afterOutcome">Called after each recorded outcome — the source reacting to it.</param>
  /// <returns>The capture start of every call made, as offsets from <see cref="T0"/> in whole seconds.</returns>
  private static List<int> Simulate(
    FingerprintCallPolicy policy,
    FakeSource source,
    Func<int, DateTimeOffset, CallOutcome> outcomeFor,
    TimeSpan duration,
    TimeSpan? captureLength = null,
    Action<DateTimeOffset>? onSecond = null,
    DateTimeOffset? from = null,
    Action<CallOutcome, FakeSource>? afterOutcome = null)
  {
    var capture = captureLength ?? Capture;
    var calls = new List<int>();
    var now = from ?? T0;
    var end = now + duration;
    while (now < end)
    {
      onSecond?.Invoke(now);
      var decision = policy.Decide(source.Snapshot, now);
      if (decision.Kind != CallDecisionKind.CallNow)
      {
        now += TimeSpan.FromSeconds(1);
        continue;
      }

      var captureStart = now;
      now += capture;
      policy.RecordCallStarted(captureStart); // as the service does: stamped at the attempt's start
      var outcome = outcomeFor(calls.Count, captureStart);
      policy.RecordOutcome(decision.Segment, captureStart, outcome, now, outcome == CallOutcome.Error ? "songrec exited with code 1" : null);
      afterOutcome?.Invoke(outcome, source);
      calls.Add((int)(captureStart - T0).TotalSeconds);
      if (capture == TimeSpan.Zero)
      {
        // An instantaneous "capture" still takes the loop a pass; never decide twice at one instant.
        now += TimeSpan.FromSeconds(1);
      }
    }

    return calls;
  }

  private static IEnumerable<int> Gaps(IReadOnlyList<int> calls) => calls.Zip(calls.Skip(1), (a, b) => b - a);

  // --- Unknown-start sources ---------------------------------------------------------------------------

  [Fact]
  public void Radio_CallsEvery15sStartToStart_WhetherOrNotTheLastCallMatched()
  {
    var policy = Create();

    var calls = Simulate(policy, Radio(), (n, _) => n % 2 == 0 ? CallOutcome.Match : CallOutcome.NoMatch,
      TimeSpan.FromMinutes(10));

    Assert.Equal(0, calls[0]);
    Assert.All(Gaps(calls), gap => Assert.Equal(15, gap));
    Assert.Equal(40, calls.Count); // 0, 15, ..., 585 — 4/minute, the 240/hour budget
  }

  [Fact]
  public void Radio_ReTune_MakesTheNextCallImmediate_AndRestartsTheSchedule()
  {
    var policy = Create();
    var radio = Radio();

    var calls = Simulate(policy, radio, (_, _) => CallOutcome.Match, TimeSpan.FromSeconds(50),
      onSecond: now =>
      {
        if (now == T0 + TimeSpan.FromSeconds(14))
        {
          policy.RequestImmediate();
        }
      });

    // 0 (start); the capture ends at 13 and the next attempt is due at 15; the re-tune at 14 makes it
    // immediate. The schedule restarts from there: 29, 44 — NOT 15, 30, 45 with the re-tune's call added.
    Assert.Equal(new[] { 0, 14, 29, 44 }, calls);
  }

  [Fact]
  public void Radio_SourceSwitchAndBack_StartsANewScheduleImmediately()
  {
    var policy = Create();
    var radio = Radio();

    var calls = Simulate(policy, radio, (_, _) => CallOutcome.Match, TimeSpan.FromSeconds(50),
      onSecond: now =>
      {
        if (now == T0 + TimeSpan.FromSeconds(14))
        {
          policy.ResetSegment();
        }
      });

    Assert.Equal(new[] { 0, 14, 29, 44 }, calls);
  }

  [Fact]
  public void Radio_CaptureThatFoundOnlySilence_LeavesTheAttemptDue()
  {
    var policy = Create();
    var radio = Radio();

    var first = policy.Decide(radio.Snapshot, T0);
    Assert.Equal(CallDecisionKind.CallNow, first.Kind);

    // The loop captured, found silence, and recorded nothing. Audio returns: the next pass calls at once.
    var returnedAt = T0 + Capture;
    var afterSilence = policy.Decide(radio.Snapshot, returnedAt);
    Assert.Equal(CallDecisionKind.CallNow, afterSilence.Kind);

    // ...and that call restarts the schedule: the next is 15 s after it.
    policy.RecordCallStarted(returnedAt + Capture);
    policy.RecordOutcome(afterSilence.Segment, returnedAt, CallOutcome.NoMatch, returnedAt + Capture);
    Assert.Equal(returnedAt + TimeSpan.FromSeconds(15), policy.Decide(radio.Snapshot, returnedAt + Capture).NotBefore);
  }

  // --- Known-start sources -----------------------------------------------------------------------------

  [Fact]
  public void File_WithCompleteMetadata_MakesNoCalls()
  {
    var policy = Create();

    var calls = Simulate(policy, FileTrack(T0, needsLookup: false), (_, _) => CallOutcome.Match,
      TimeSpan.FromMinutes(10));

    Assert.Empty(calls);
    Assert.Equal(CallDecisionKind.NotNeeded, policy.Decide(FileTrack(T0, false).Snapshot, T0).Kind);
  }

  [Theory]
  [InlineData(PlaySource.File)]
  [InlineData(PlaySource.Bluetooth)]
  public void KnownStart_MissingArt_OneCallAfterTheFirstCallDelay_ThenValidatesEvery60s(PlaySource type)
  {
    var policy = Create();
    var source = FileTrack(T0, needsLookup: true);
    source.Type = type;

    // A match fills the art, so the source stops asking — the validations come from the policy alone.
    var calls = Simulate(policy, source, (_, _) => CallOutcome.Match, TimeSpan.FromMinutes(4),
      afterOutcome: (outcome, s) => s.NeedsLookup = outcome != CallOutcome.Match && s.NeedsLookup);

    Assert.Equal(new[] { 5, 65, 125, 185 }, calls);
  }

  [Fact]
  public void KnownStart_NoMatch_RetriesAfter30s_ThenEvery60s()
  {
    var policy = Create();

    var calls = Simulate(policy, FileTrack(T0, needsLookup: true), (_, _) => CallOutcome.NoMatch,
      TimeSpan.FromMinutes(4));

    Assert.Equal(new[] { 5, 35, 95, 155, 215 }, calls);
  }

  [Fact]
  public void KnownStart_NoMatchThenMatch_SwitchesToTheValidationInterval()
  {
    var policy = Create();

    var calls = Simulate(policy, FileTrack(T0, needsLookup: true),
      (n, _) => n == 0 ? CallOutcome.NoMatch : CallOutcome.Match, TimeSpan.FromMinutes(3));

    Assert.Equal(new[] { 5, 35, 95, 155 }, calls);
  }

  [Fact]
  public void KnownStart_TrackChange_ResetsTheSchedule()
  {
    var policy = Create();
    var source = FileTrack(T0, needsLookup: true);
    var secondTrackStart = T0 + TimeSpan.FromSeconds(100);

    var calls = Simulate(policy, source, (_, _) => CallOutcome.Match, TimeSpan.FromSeconds(200),
      onSecond: now =>
      {
        if (now == secondTrackStart)
        {
          source.TrackStartedUtc = secondTrackStart.UtcDateTime;
          source.NeedsLookup = true;
        }
      },
      afterOutcome: (outcome, s) => s.NeedsLookup = false);

    // Track 1: 5, 65 (validation). Track 2 starts at 100: its first call is 105 — not track 1's 125.
    Assert.Equal(new[] { 5, 65, 105, 165 }, calls);
  }

  [Fact]
  public void KnownStart_TrackStartedLongAgo_IsDueImmediately()
  {
    var policy = Create();
    var source = FileTrack(T0 - TimeSpan.FromMinutes(3), needsLookup: true);

    Assert.Equal(CallDecisionKind.CallNow, policy.Decide(source.Snapshot, T0).Kind);
  }

  [Fact]
  public void KnownStart_ImmediateRequest_DoesNotBypassTheFirstCallDelay()
  {
    var policy = Create();
    var source = FileTrack(T0, needsLookup: true);

    policy.RequestImmediate();
    var decision = policy.Decide(source.Snapshot, T0);

    Assert.Equal(CallDecisionKind.Wait, decision.Kind);
    Assert.Equal(T0 + TimeSpan.FromSeconds(5), decision.NotBefore);
  }

  // --- Hourly cap --------------------------------------------------------------------------------------

  [Fact]
  public void Cap_The241stCallInARollingHourWaits_UntilTheOldestAgesOut_AndWarnsOncePerEpisode()
  {
    // A radio interval of 1 s would make ~3,600 calls an hour; the default cap must hold it to 240.
    var policy = Create(new FingerprintingOptions { UnknownStartIntervalSeconds = 1 });

    var calls = Simulate(policy, Radio(), (_, _) => CallOutcome.Match, TimeSpan.FromHours(2),
      captureLength: TimeSpan.Zero);

    // The first 240 go at once (one per simulated second); the 241st waits until the first is an hour old.
    Assert.Equal(Enumerable.Range(0, 240), calls.Take(240));
    Assert.Equal(3600, calls[240]);

    // Every rolling hour holds at most 240 calls.
    for (var i = 240; i < calls.Count; i++)
    {
      Assert.True(calls[i] - calls[i - 240] >= 3600, $"calls {i - 240}..{i} fit in under an hour");
    }

    // Saturated for the whole run: one episode, one Warning — not one per held attempt or freed slot.
    Assert.Single(_logs, l => l.Level == LogLevel.Warning && l.Message.Contains("call cap reached"));
  }

  [Fact]
  public void Cap_ANewExhaustionAfterDemandDropped_IsANewEpisode()
  {
    var options = new FingerprintingOptions { UnknownStartIntervalSeconds = 1, MaxCallsPerHour = 10 };
    var policy = Create(options);
    var radio = Radio();

    // Exhaust it once.
    Simulate(policy, radio, (_, _) => CallOutcome.Match, TimeSpan.FromSeconds(30), captureLength: TimeSpan.Zero);
    Assert.Single(_logs, l => l.Level == LogLevel.Warning);

    // Demand drops (slow interval) while the window drains, then rises again.
    options.UnknownStartIntervalSeconds = 1200;
    Simulate(policy, radio, (_, _) => CallOutcome.Match, TimeSpan.FromHours(2), captureLength: TimeSpan.Zero,
      from: T0 + TimeSpan.FromSeconds(30));
    options.UnknownStartIntervalSeconds = 1;
    policy.ResetSegment(); // e.g. a source switch: due at once, on the new 1 s interval
    Simulate(policy, radio, (_, _) => CallOutcome.Match, TimeSpan.FromSeconds(60), captureLength: TimeSpan.Zero,
      from: T0 + TimeSpan.FromHours(2) + TimeSpan.FromSeconds(30));

    Assert.Equal(2, _logs.Count(l => l.Level == LogLevel.Warning && l.Message.Contains("call cap reached")));
  }

  /// <summary>
  /// Review finding: the default schedule (15 s on a 13 s capture) runs at exactly the default cap's rate,
  /// so the cap must not bind on ordinary listening — no held attempt, no Warning, over hours.
  /// </summary>
  [Fact]
  public void Cap_DoesNotBind_OnTheDefaultRadioSchedule()
  {
    var policy = Create();

    var calls = Simulate(policy, Radio(), (_, _) => CallOutcome.Match, TimeSpan.FromHours(3));

    Assert.All(Gaps(calls), gap => Assert.Equal(15, gap));
    Assert.Equal(720, calls.Count);
    Assert.DoesNotContain(_logs, l => l.Level == LogLevel.Warning);
  }

  [Fact]
  public void Cap_IsGlobal_ASourceSwitchDoesNotResetIt()
  {
    var policy = Create(new FingerprintingOptions { MaxCallsPerHour = 2, UnknownStartIntervalSeconds = 1 });
    var radio = Radio();

    Simulate(policy, radio, (_, _) => CallOutcome.Match, TimeSpan.FromSeconds(10), captureLength: TimeSpan.Zero);
    policy.ResetSegment();
    var vinyl = new FakeSource(PlaySource.Vinyl, "Vinyl");

    var decision = policy.Decide(vinyl.Snapshot, T0 + TimeSpan.FromSeconds(10));

    Assert.Equal(CallDecisionKind.Wait, decision.Kind);
    Assert.Equal(CallBlocker.HourlyCap, decision.Blocker);
  }

  // --- Failures ----------------------------------------------------------------------------------------

  [Fact]
  public void Errors_BackOffExponentially_From30sTo600s()
  {
    // A 1 s radio interval so the back-off, not the schedule, is what spaces the attempts.
    var policy = Create(new FingerprintingOptions { UnknownStartIntervalSeconds = 1 });

    var calls = Simulate(policy, Radio(), (_, _) => CallOutcome.Error, TimeSpan.FromMinutes(45),
      captureLength: TimeSpan.Zero);

    Assert.Equal(new[] { 30, 60, 120, 240, 480, 600, 600 }, Gaps(calls).Take(7));
  }

  [Fact]
  public void Errors_AreResetByTheNextCleanCall_AndANoMatchCountsAsClean()
  {
    var policy = Create(new FingerprintingOptions { UnknownStartIntervalSeconds = 1 });

    // Three failures, then a clean no-match, then the schedule's own 1 s interval resumes.
    var calls = Simulate(policy, Radio(), (n, _) => n < 3 ? CallOutcome.Error : CallOutcome.NoMatch,
      TimeSpan.FromSeconds(260), captureLength: TimeSpan.Zero);

    Assert.Equal(new[] { 30, 60, 120 }, Gaps(calls).Take(3));
    Assert.Equal(new[] { 1, 1, 1 }, Gaps(calls).Skip(3).Take(3));
    Assert.Equal(0, policy.ConsecutiveErrors);
  }

  [Fact]
  public void Errors_WarnOnceAfterFiveInARow_AndAgainOnlyAfterARecovery()
  {
    var policy = Create(new FingerprintingOptions { UnknownStartIntervalSeconds = 1, ErrorBackoffInitialSeconds = 1, ErrorBackoffMaxSeconds = 1 });
    var radio = Radio();
    var warnings = () => _logs.Count(l => l.Level == LogLevel.Warning && l.Message.Contains("failed"));

    var at = T0;
    void Fail()
    {
      var d = policy.Decide(radio.Snapshot, at);
      Assert.Equal(CallDecisionKind.CallNow, d.Kind);
      policy.RecordCallStarted(at);
      policy.RecordOutcome(d.Segment, at, CallOutcome.Error, at, "timed out");
      at += TimeSpan.FromSeconds(2);
    }

    for (var i = 0; i < 4; i++)
    {
      Fail();
    }
    Assert.Equal(0, warnings());

    Fail();
    Assert.Equal(1, warnings());

    for (var i = 0; i < 5; i++)
    {
      Fail();
    }
    Assert.Equal(1, warnings());

    var ok = policy.Decide(radio.Snapshot, at);
    policy.RecordOutcome(ok.Segment, at, CallOutcome.Match, at);
    at += TimeSpan.FromSeconds(2);
    for (var i = 0; i < 5; i++)
    {
      Fail();
    }
    Assert.Equal(2, warnings());
  }

  [Fact]
  public void Errors_HoldEverySource_NotJustTheOneThatFailed()
  {
    var policy = Create();
    var radio = Radio();

    var d = policy.Decide(radio.Snapshot, T0);
    policy.RecordCallStarted(T0);
    policy.RecordOutcome(d.Segment, T0, CallOutcome.Error, T0 + Capture, "timed out");
    policy.ResetSegment();

    var file = FileTrack(T0 - TimeSpan.FromMinutes(1), needsLookup: true);
    var decision = policy.Decide(file.Snapshot, T0 + Capture);

    Assert.Equal(CallDecisionKind.Wait, decision.Kind);
    Assert.Equal(CallBlocker.ErrorBackoff, decision.Blocker);
    Assert.Equal(T0 + Capture + TimeSpan.FromSeconds(30), decision.NotBefore);
  }

  // --- Options -----------------------------------------------------------------------------------------

  /// <summary>The owner's numbers (2026-10-07, as revised the same day), pinned so a default cannot drift.</summary>
  [Fact]
  public void Defaults_AreTheOwnersPolicy()
  {
    var o = new FingerprintingOptions();

    Assert.Equal(13, o.SampleDurationSeconds);
    Assert.Equal(15, o.UnknownStartIntervalSeconds);
    Assert.Equal(240, o.MaxCallsPerHour);
    Assert.Equal(5, o.KnownStartFirstCallDelaySeconds);
    Assert.Equal(30, o.KnownStartFirstRetryDelaySeconds);
    Assert.Equal(60, o.KnownStartRetryIntervalSeconds);
    Assert.Equal(60, o.KnownStartValidationIntervalSeconds);
    Assert.Equal(30, o.ErrorBackoffInitialSeconds);
    Assert.Equal(600, o.ErrorBackoffMaxSeconds);
    Assert.Equal(5, o.ErrorWarnThreshold);
  }


  [Fact]
  public void Options_AreReadLive_ByTheNextDecision()
  {
    var options = new FingerprintingOptions();
    var policy = Create(options);
    var radio = Radio();

    var d = policy.Decide(radio.Snapshot, T0);
    policy.RecordCallStarted(T0 + Capture);
    options.UnknownStartIntervalSeconds = 90;
    policy.RecordOutcome(d.Segment, T0, CallOutcome.Match, T0 + Capture);

    Assert.Equal(T0 + TimeSpan.FromSeconds(90), policy.Decide(radio.Snapshot, T0 + Capture).NotBefore);
  }

  private sealed class ListLogger(List<(LogLevel Level, string Message)> sink) : ILogger
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) => sink.Add((logLevel, formatter(state, exception)));
  }
}
