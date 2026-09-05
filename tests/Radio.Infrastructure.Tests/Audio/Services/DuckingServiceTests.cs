using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

public class DuckingServiceTests
{
  // Construction lives in DuckingServiceFixture, shared with
  // DuckingServiceCharacterizationTests so the two cannot drift apart about what today's
  // ducking behaviour is.
  private readonly DuckingServiceFixture _fixture;
  private readonly Mock<ILogger<DuckingService>> _loggerMock;
  private readonly Mock<IOptionsMonitor<AudioOptions>> _optionsMock;
  private readonly Mock<IMasterMixer> _mixerMock;
  private readonly AudioOptions _defaultOptions;

  public DuckingServiceTests()
  {
    _fixture = new DuckingServiceFixture();
    _loggerMock = _fixture.LoggerMock;
    _optionsMock = _fixture.OptionsMock;
    _mixerMock = _fixture.MixerMock;
    _defaultOptions = _fixture.Options;
  }

  private DuckingService CreateService() => _fixture.CreateService();

  private Mock<IEventAudioSource> CreateMockEventSource(string? id = null) =>
    _fixture.CreateMockEventSource(id);

  private Mock<IAudioSource> CreateMockPrimarySource(string? id = null) =>
    _fixture.CreateMockPrimarySource(id);

  [Fact]
  public void Constructor_ThrowsOnNullLogger()
  {
    Assert.Throws<ArgumentNullException>(
      () => new DuckingService(null!, _optionsMock.Object, _mixerMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullOptions()
  {
    Assert.Throws<ArgumentNullException>(
      () => new DuckingService(_loggerMock.Object, null!, _mixerMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullMixer()
  {
    Assert.Throws<ArgumentNullException>(
      () => new DuckingService(_loggerMock.Object, _optionsMock.Object, null!));
  }

  [Fact]
  public void InitialState_IsDuckingFalse()
  {
    var service = CreateService();

    Assert.False(service.IsDucking);
  }

  [Fact]
  public void InitialState_CurrentDuckLevelIs100()
  {
    var service = CreateService();

    Assert.Equal(100f, service.CurrentDuckLevel);
  }

  [Fact]
  public void InitialState_ActiveEventCountIsZero()
  {
    var service = CreateService();

    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_ThrowsOnNullSource()
  {
    var service = CreateService();

    await Assert.ThrowsAsync<ArgumentNullException>(
      () => service.StartDuckingAsync(null!));
  }

  [Fact]
  public async Task StartDuckingAsync_SetsIsDuckingTrue()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    // Use Instant policy for immediate effect
    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.True(service.IsDucking);
  }

  [Fact]
  public async Task StartDuckingAsync_IncrementsActiveEventCount()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.Equal(1, service.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_InstantPolicy_SetsDuckLevelImmediately()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;
    _defaultOptions.DuckingPercentage = 25;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.Equal(25f, service.CurrentDuckLevel);
  }

  [Fact]
  public async Task StartDuckingAsync_RaisesDuckingStateChangedEvent()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    DuckingStateChangedEventArgs? capturedArgs = null;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, args) => capturedArgs = args;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.NotNull(capturedArgs);
    Assert.True(capturedArgs.IsDucking);
    Assert.Same(eventSource.Object, capturedArgs.TriggeringSource);
    Assert.Equal(1, capturedArgs.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_MultipleEvents_DoesNotChangeTheDuckLevel_ButAnnouncesEachSource()
  {
    // ⚠ WAS StartDuckingAsync_MultipleEvents_DoesNotDuckAgain, asserting stateChangeCount == 1.
    // This is the SECOND tripwire for ADR-029 D5 and it lives outside
    // DuckingServiceCharacterizationTests, which is the only one the PHN-1a/1b/1c handoffs named. The
    // name changed with the assertion: the service still does not duck again — the level is unmoved —
    // but it now announces the second source.
    var service = CreateService();
    var eventSource1 = CreateMockEventSource("source1");
    var eventSource2 = CreateMockEventSource("source2");
    var stateChangeCount = 0;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, _) => stateChangeCount++;

    await service.StartDuckingAsync(eventSource1.Object);
    var levelAfterFirst = service.CurrentDuckLevel;
    await service.StartDuckingAsync(eventSource2.Object);

    Assert.Equal(2, stateChangeCount);
    Assert.Equal(2, service.ActiveEventCount);
    Assert.Equal(levelAfterFirst, service.CurrentDuckLevel);
  }

  [Fact]
  public async Task StopDuckingAsync_ThrowsOnNullSource()
  {
    var service = CreateService();

    await Assert.ThrowsAsync<ArgumentNullException>(
      () => service.StopDuckingAsync(null!));
  }

  [Fact]
  public async Task StopDuckingAsync_RestoresFullVolume_WhenLastEvent()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource.Object);
    await service.StopDuckingAsync(eventSource.Object);

    Assert.False(service.IsDucking);
    Assert.Equal(100f, service.CurrentDuckLevel);
    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public async Task StopDuckingAsync_DoesNotRestoreVolume_WhenOtherEventsActive()
  {
    var service = CreateService();
    var eventSource1 = CreateMockEventSource("source1");
    var eventSource2 = CreateMockEventSource("source2");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource1.Object);
    await service.StartDuckingAsync(eventSource2.Object);
    await service.StopDuckingAsync(eventSource1.Object);

    Assert.True(service.IsDucking);
    Assert.Equal(_defaultOptions.DuckingPercentage, service.CurrentDuckLevel);
    Assert.Equal(1, service.ActiveEventCount);
  }

  [Fact]
  public async Task StopDuckingAsync_RaisesDuckingStateChangedEvent_WhenLastEvent()
  {
    // ⚠ THIS TEST ENCODED THE OLD RULE AND IS UPDATED RATHER THAN DELETED. Before PHN-1f the raise
    // below was the ONLY one StopDuckingAsync ever made — it lived inside `if (needsRestore)`, so a
    // source leaving while others remained produced nothing at all. It now raises on every removal,
    // and what this test still pins is the EMPTYING case: exactly two raises for one start and one
    // stop, and IsDucking false on the second because the set really is empty.
    //
    // ASourceLeavingWhileOthersRemainStillRaises covers the case this one cannot see, and
    // AnEndedRaiseWithOtherSourcesStillActiveReportsIsDuckingTrue covers the aggregate on that path.
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var stateChanges = new List<DuckingStateChangedEventArgs>();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, args) => stateChanges.Add(args);

    await service.StartDuckingAsync(eventSource.Object);
    await service.StopDuckingAsync(eventSource.Object);

    Assert.Equal(2, stateChanges.Count);
    Assert.True(stateChanges[0].IsDucking);  // Start ducking
    Assert.False(stateChanges[1].IsDucking); // Stop ducking
    Assert.Equal(DuckingSourceTransition.Started, stateChanges[0].Transition);
    Assert.Equal(DuckingSourceTransition.Ended, stateChanges[1].Transition);
  }

  [Fact]
  public async Task AnEndedRaiseCarriesThePriorityTheSourceHadBeforeItWasRemoved()
  {
    // ⭐ THE CAPTURE, on the stop path. StopDuckingAsync deletes the _sourcePriorities override in the
    // same lock that removes the source, so a subscriber resolving the priority for itself reads the
    // category default 8 for an announcement whose caller explicitly claimed 3 — and would then read
    // an ending announcement as a priority-8 preemption.
    //
    // MUTATION (§2.1): move the `priorityBeforeRemoval = GetPriority(eventSource)` capture below
    // `_sourcePriorities.Remove(eventSource.Id)` and this reads 8 instead of 3.
    var service = CreateService();
    var eventSource = CreateMockEventSource("announcement").Object;
    DuckingStateChangedEventArgs? ended = null;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.SetPriority(eventSource, 3);
    await service.StartDuckingAsync(eventSource);

    service.DuckingStateChanged += (_, args) =>
    {
      if (args.Transition == DuckingSourceTransition.Ended) { ended = args; }
    };

    await service.StopDuckingAsync(eventSource);

    Assert.NotNull(ended);
    Assert.Equal(3, ended!.TriggeringSourcePriority);
    Assert.Same(eventSource, ended.TriggeringSource);

    // …and the service itself really has forgotten it by now, which is what makes the captured value
    // the only place the claimed priority survives.
    Assert.Equal(DuckingService.DefaultEventPriority, service.GetPriority(eventSource));
  }

  [Fact]
  public async Task AnEndedRaiseWithOtherSourcesStillActiveReportsIsDuckingTrue()
  {
    // ⚠ THE HAZARD GUARD, and it is the COMBINATION of the two fields that guards it rather than
    // either alone. AudioManager keys ClearDuckingMultiplier off the IsDucking:false edge, so raising
    // false here — while a second announcement is still sounding — would restore the radio to full
    // volume MID-ANNOUNCEMENT. Transition is what lets "a source ended" be said without saying
    // "ducking ended", and that is the whole reason the field exists.
    //
    // MUTATION (§2.1): raise `isDucking: false` unconditionally instead of `!needsRestore`.
    var service = CreateService();
    var first = CreateMockEventSource("source1").Object;
    var second = CreateMockEventSource("source2").Object;
    DuckingStateChangedEventArgs? ended = null;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(first);
    await service.StartDuckingAsync(second);

    service.DuckingStateChanged += (_, args) =>
    {
      if (args.Transition == DuckingSourceTransition.Ended) { ended = args; }
    };

    await service.StopDuckingAsync(first);

    Assert.NotNull(ended);
    Assert.True(ended!.IsDucking);
    Assert.Equal(DuckingSourceTransition.Ended, ended.Transition);
    Assert.Equal(1, ended.ActiveEventCount);
    Assert.Same(first, ended.TriggeringSource);

    // The aggregate on the args agrees with the service, so neither is guessing.
    Assert.True(service.IsDucking);
  }

  [Fact]
  public async Task AStartedRaiseCarriesThePriorityCapturedBeforeTheAttackFade()
  {
    // ⭐ THE CAPTURE, on the start path, and the fade window is the reason it has to be a capture.
    // DuckingService raises the Started transition AFTER awaiting ApplyFadeAsync
    // (Audio:DuckingAttackMs, 100 ms shipped), so a StopDuckingAsync for the SAME source landing
    // inside that fade deletes the override before the raise fires. PHN-1d could only narrow that with
    // an ActiveEventCount guard whose own residual it documented; capturing before the fade closes it.
    //
    // ⚠ THE RENDEZVOUS IS A LEVEL CHANGE, NOT A DELAY (CLAUDE.md § Test Timing). The stop is performed
    // from inside the first mid-fade DuckingLevelChanged raise, which is a synchronous observation of
    // "the fade has started and has not finished" — so the stop is inside the window by construction
    // rather than by being fast enough. The fade's own Task.Delay is awaited by the method under test,
    // not raced by an assertion.
    //
    // The stop is driven synchronously and the policy is flipped to Instant first so its own release
    // fade cannot await: StopDuckingAsync reads DuckingPolicy at entry, and the start's ApplyFadeAsync
    // already took its policy by value, so the flip cannot reach back into it.
    //
    // MUTATION (§2.1): move the `priorityAtStart = GetPriority(eventSource)` capture below
    // ApplyFadeAsync and this reads 8 instead of 3.
    var service = CreateService();
    var eventSource = CreateMockEventSource("announcement").Object;
    DuckingStateChangedEventArgs? started = null;
    var stoppedInsideTheFade = false;

    _defaultOptions.DuckingPolicy = DuckingPolicy.FadeSmooth;
    _defaultOptions.DuckingAttackMs = 100;

    service.SetPriority(eventSource, 3);

    service.DuckingStateChanged += (_, args) =>
    {
      if (args.Transition == DuckingSourceTransition.Started) { started = args; }
    };

    service.DuckingLevelChanged += (_, level) =>
    {
      if (stoppedInsideTheFade || level.TransitionComplete)
      {
        return;
      }

      stoppedInsideTheFade = true;
      _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;
      service.StopDuckingAsync(eventSource).GetAwaiter().GetResult();
    };

    await service.StartDuckingAsync(eventSource);

    // ⚠ Asserted first, and it is not decoration: if the fade collapsed to a single step this handler
    // never fires, the override is never deleted, and every assertion below would pass under the
    // mutation too. This is the line that keeps the test honest.
    Assert.True(
      stoppedInsideTheFade,
      "the fade must have produced at least one mid-transition level change for this test to mean anything");

    Assert.NotNull(started);
    Assert.Equal(3, started!.TriggeringSourcePriority);

    // The override really was gone by the time the raise fired, which is exactly what a subscriber
    // resolving the priority for itself would have read.
    Assert.Equal(DuckingService.DefaultEventPriority, service.GetPriority(eventSource));
  }

  [Fact]
  public async Task StopAllDuckingRaisesAllClearedWithANullSource()
  {
    // AllCleared is its own member rather than an Ended with a null source, because "everything went
    // away at once" is the strongest reason a D28 wait should end and a subscriber should not have to
    // infer it from a null. EventPlaybackService.TryWakeWaitingPlayback runs above the transition test
    // for exactly this raise.
    var service = CreateService();
    var first = CreateMockEventSource("source1").Object;
    var second = CreateMockEventSource("source2").Object;
    DuckingStateChangedEventArgs? cleared = null;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(first);
    await service.StartDuckingAsync(second);

    service.DuckingStateChanged += (_, args) => cleared = args;

    await service.StopAllDuckingAsync();

    Assert.NotNull(cleared);
    Assert.Equal(DuckingSourceTransition.AllCleared, cleared!.Transition);
    Assert.Null(cleared.TriggeringSource);
    Assert.False(cleared.IsDucking);
    Assert.Equal(0, cleared.ActiveEventCount);

    // Zero rather than the category default, because there is no triggering source to have a priority.
    Assert.Equal(0, cleared.TriggeringSourcePriority);
  }

  [Fact]
  public async Task StopAllDuckingAsync_RestoresFullVolume()
  {
    var service = CreateService();
    var eventSource1 = CreateMockEventSource("source1");
    var eventSource2 = CreateMockEventSource("source2");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource1.Object);
    await service.StartDuckingAsync(eventSource2.Object);

    await service.StopAllDuckingAsync();

    Assert.False(service.IsDucking);
    Assert.Equal(100f, service.CurrentDuckLevel);
    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public void GetPriority_ReturnsDefaultForEventSource()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    var priority = service.GetPriority(eventSource.Object);

    Assert.Equal(DuckingService.DefaultEventPriority, priority);
  }

  [Fact]
  public void GetPriority_ReturnsDefaultForPrimarySource()
  {
    var service = CreateService();
    var primarySource = CreateMockPrimarySource();

    var priority = service.GetPriority(primarySource.Object);

    Assert.Equal(DuckingService.DefaultPrimaryPriority, priority);
  }

  [Fact]
  public void SetPriority_ThrowsOnNullSource()
  {
    var service = CreateService();

    Assert.Throws<ArgumentNullException>(
      () => service.SetPriority(null!, 5));
  }

  [Fact]
  public void SetPriority_ThrowsOnOutOfRangePriority()
  {
    var service = CreateService();
    var source = CreateMockEventSource();

    Assert.Throws<ArgumentOutOfRangeException>(
      () => service.SetPriority(source.Object, 0));

    Assert.Throws<ArgumentOutOfRangeException>(
      () => service.SetPriority(source.Object, 11));
  }

  [Fact]
  public void SetPriority_SetsPriority()
  {
    var service = CreateService();
    var source = CreateMockEventSource();

    service.SetPriority(source.Object, 7);

    Assert.Equal(7, service.GetPriority(source.Object));
  }

  [Fact]
  public async Task StopDuckingAsync_RemovesPerSourcePriorityOverride()
  {
    // Regression test for the _sourcePriorities leak: AnnouncementService sets a
    // per-source priority (keyed by a fresh GUID id) before every ducking cycle.
    // StopDuckingAsync must drop that entry so the map does not grow forever.
    var service = CreateService();
    var eventSource = CreateMockEventSource("ephemeral-announcement");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.SetPriority(eventSource.Object, 7);
    Assert.Equal(7, service.GetPriority(eventSource.Object)); // override in place

    await service.StartDuckingAsync(eventSource.Object);
    await service.StopDuckingAsync(eventSource.Object);

    // Override must be gone — GetPriority falls back to the category default,
    // proving the entry was evicted from _sourcePriorities rather than lingering.
    Assert.Equal(DuckingService.DefaultEventPriority, service.GetPriority(eventSource.Object));
  }

  [Fact]
  public async Task RepeatedDuckingCycles_DoNotAccumulatePriorityEntries()
  {
    // Simulates many TTS/notification announcements, each with a unique source id.
    // Before the fix every cycle left a permanent _sourcePriorities entry; after it,
    // each source's override is released on stop so nothing accumulates. We assert
    // the observable proxy: every stopped source reverts to its default priority.
    var service = CreateService();
    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    for (int i = 0; i < 50; i++)
    {
      var source = CreateMockEventSource($"announcement-{i}");
      service.SetPriority(source.Object, 6);
      await service.StartDuckingAsync(source.Object);
      await service.StopDuckingAsync(source.Object);

      Assert.Equal(DuckingService.DefaultEventPriority, service.GetPriority(source.Object));
    }

    Assert.Equal(0, service.ActiveEventCount);
    Assert.False(service.IsDucking);
  }

  [Fact]
  public async Task GetActiveEventsByPriority_ReturnsSortedList()
  {
    var service = CreateService();
    var lowPriority = CreateMockEventSource("low");
    var highPriority = CreateMockEventSource("high");
    var mediumPriority = CreateMockEventSource("medium");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.SetPriority(lowPriority.Object, 3);
    service.SetPriority(highPriority.Object, 9);
    service.SetPriority(mediumPriority.Object, 5);

    await service.StartDuckingAsync(lowPriority.Object);
    await service.StartDuckingAsync(highPriority.Object);
    await service.StartDuckingAsync(mediumPriority.Object);

    var activeEvents = service.GetActiveEventsByPriority();

    Assert.Equal(3, activeEvents.Count);
    Assert.Same(highPriority.Object, activeEvents[0]);
    Assert.Same(mediumPriority.Object, activeEvents[1]);
    Assert.Same(lowPriority.Object, activeEvents[2]);
  }

  [Fact]
  public async Task DuckingLevelChanged_IsRaisedDuringFade()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var levelChanges = new List<DuckingLevelChangedEventArgs>();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingLevelChanged += (_, args) => levelChanges.Add(args);

    await service.StartDuckingAsync(eventSource.Object);

    Assert.NotEmpty(levelChanges);
    Assert.True(levelChanges.Last().TransitionComplete);
  }

  [Fact]
  public async Task FadeSmooth_ProducesMultipleLevelChanges()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var levelChanges = new List<DuckingLevelChangedEventArgs>();

    _defaultOptions.DuckingPolicy = DuckingPolicy.FadeSmooth;
    _defaultOptions.DuckingAttackMs = 100; // Short for testing

    service.DuckingLevelChanged += (_, args) => levelChanges.Add(args);

    await service.StartDuckingAsync(eventSource.Object);

    // FadeSmooth should produce multiple level changes
    Assert.True(levelChanges.Count > 1);
    Assert.True(levelChanges.Last().TransitionComplete);
  }

  [Fact]
  public async Task FadeQuick_ProducesSomeLevelChanges()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var levelChanges = new List<DuckingLevelChangedEventArgs>();

    _defaultOptions.DuckingPolicy = DuckingPolicy.FadeQuick;
    _defaultOptions.DuckingAttackMs = 100;

    service.DuckingLevelChanged += (_, args) => levelChanges.Add(args);

    await service.StartDuckingAsync(eventSource.Object);

    Assert.NotEmpty(levelChanges);
    Assert.True(levelChanges.Last().TransitionComplete);
  }

  [Fact]
  public void Dispose_ClearsActiveEvents()
  {
    var service = CreateService();

    service.Dispose();

    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_ThrowsWhenDisposed()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    service.Dispose();

    await Assert.ThrowsAsync<ObjectDisposedException>(
      () => service.StartDuckingAsync(eventSource.Object));
  }

  [Fact]
  public async Task StopDuckingAsync_ThrowsWhenDisposed()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    service.Dispose();

    await Assert.ThrowsAsync<ObjectDisposedException>(
      () => service.StopDuckingAsync(eventSource.Object));
  }

  [Fact]
  public async Task StopAllDuckingAsync_ThrowsWhenDisposed()
  {
    var service = CreateService();

    service.Dispose();

    await Assert.ThrowsAsync<ObjectDisposedException>(
      () => service.StopAllDuckingAsync());
  }

  [Fact]
  public void Dispose_CanBeCalledMultipleTimes()
  {
    var service = CreateService();

    service.Dispose();
    service.Dispose(); // Should not throw
  }

  [Fact]
  public async Task DuplicateStartDucking_DoesNotAddDuplicateEvents()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource("same-source");

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(eventSource.Object);
    await service.StartDuckingAsync(eventSource.Object);

    Assert.Equal(1, service.ActiveEventCount);
  }

  [Fact]
  public async Task StopDucking_ForNonActiveEvent_DoesNotThrow()
  {
    var service = CreateService();
    var eventSource = CreateMockEventSource();

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    // Stop without starting should not throw
    await service.StopDuckingAsync(eventSource.Object);

    Assert.Equal(0, service.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_SurvivesASubscriberThatThrows()
  {
    // PR 4 adds the first DuckingStateChanged subscriber that does work the caller depends on. (It is
    // not the first that CAN throw: AudioManager's handler reaches ThrowIfDisposed at shutdown.)
    // Unguarded, the exception propagates out of StartDuckingAsync into
    // AnnouncementService.AnnounceAsync, which catches it and cleans up — so ducking is restored and
    // nothing is stuck, but the announcement never plays AND POST /api/notifications/announce still
    // answers 200. A fault in the attended seam would silence the unattended one, invisibly.
    var service = CreateService();
    var eventSource = CreateMockEventSource();
    var reached = false;

    _defaultOptions.DuckingPolicy = DuckingPolicy.Instant;

    service.DuckingStateChanged += (_, _) => throw new InvalidOperationException("subscriber is broken");
    service.DuckingStateChanged += (_, _) => reached = true;

    await service.StartDuckingAsync(eventSource.Object);

    Assert.True(service.IsDucking);
    Assert.Equal(1, service.ActiveEventCount);
    // A later subscriber does NOT still run: .NET stops the invocation list at the first handler that
    // throws, and a single try around Invoke catches the exception without resuming it. The assertion
    // is written against that honest behaviour rather than a stronger one — overclaiming in a test is
    // the same failure class as overclaiming in a comment. PR 4 has exactly two subscribers and does
    // not need per-handler isolation; if a future PR does, that is a GetInvocationList loop and its own
    // decision.
    Assert.False(reached, "documented: a throwing handler ends the invocation list; the guard stops the "
      + "exception escaping StartDuckingAsync, it does not resume the list");
  }
}
