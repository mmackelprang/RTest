using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// CHARACTERIZATION tests: these assert what DuckingService does, deliberately at a level of detail
/// an ordinary unit test would not bother with, so that a change to this shared audio service shows
/// up as an edited assertion in someone's diff rather than as a silent behavioural shift.
///
/// They were written by PHN-1a for ADR-029 D5 / PHN arc PR 4. ⚠ PR 4 HAS NOW LANDED and the second
/// test below is the one it changed: a second concurrent event used to raise nothing and now raises
/// once. Ducking itself is still binary and reference-counted — the duck LEVEL is still the fixed
/// global Audio:DuckingPercentage regardless of priority, which is what the third test still pins.
/// What changed is that the service now ANNOUNCES each source that joins the set, so a subscriber
/// can arbitrate on priority. It does not arbitrate on priority itself and PR 4 did not make it.
///
/// ⚠ PHN-1f (PR 5b) CHANGED THE OTHER HALF, and ASourceLeavingWhileOthersRemainStillRaises is the
/// test it added. StopDuckingAsync used to raise ONLY when the set emptied, so a source leaving while
/// others remained produced nothing at all — and a subscriber cannot act on a source ending if it is
/// never told one did, which is what starved D28's wait-then-play queue. It now raises on every
/// removal, with IsDucking carrying the TRUE AGGREGATE and DuckingSourceTransition saying which of the
/// two things happened. Still no arbitration here; still binary and reference-counted.
///
/// ⚠ If you are changing these again: update them, do not delete them.
/// </summary>
public class DuckingServiceCharacterizationTests
{
  private readonly DuckingServiceFixture _fixture = new();

  private DuckingService CreateService() => _fixture.CreateService();

  private IEventAudioSource CreateEventSource(string id) => _fixture.CreateEventSource(id);

  [Fact]
  public async Task StartDuckingAsync_RaisesDuckingStateChanged_OnTheFirstEvent()
  {
    var service = CreateService();
    var raised = 0;
    service.DuckingStateChanged += (_, _) => raised++;

    await service.StartDuckingAsync(CreateEventSource("event-1"));

    Assert.Equal(1, raised);
  }

  [Fact]
  public async Task StartDuckingAsync_RaisesOncePerSourceThatJoinsTheSet()
  {
    // ⚠ WAS StartDuckingAsync_DoesNotRaise_ForASecondConcurrentEvent_TODAY, asserting 0. ADR-029
    // §6.3 required this to become 1 and PR 4 is the change: the raise moved out of the
    // if (needsTransition) branch, so a second concurrent event is announced instead of reaching only
    // a LogDebug. Without this, EventPlaybackService could never learn that a priority-9 ring had
    // started while a voicemail was already ducking — which is the whole of D5.
    var service = CreateService();
    await service.StartDuckingAsync(CreateEventSource("event-1"));

    var raisedAfterFirst = 0;
    DuckingStateChangedEventArgs? last = null;
    service.DuckingStateChanged += (_, args) => { raisedAfterFirst++; last = args; };

    var second = CreateEventSource("event-2");
    await service.StartDuckingAsync(second);

    Assert.Equal(1, raisedAfterFirst);
    Assert.NotNull(last);
    Assert.True(last.IsDucking);
    // The identity of the STARTING source is the load-bearing field: EventPlaybackService reads its
    // priority from it, and reads it synchronously because StopDuckingAsync later deletes the entry.
    Assert.Same(second, last.TriggeringSource);
    Assert.Equal(2, last.ActiveEventCount);
  }

  [Fact]
  public async Task StartDuckingAsync_DoesNotRaise_ForASourceAlreadyInTheSet()
  {
    // The boundary of PR 4's change, pinned so it cannot silently widen to "every call". ADR-029
    // §6.3 says "every StartDuckingAsync"; this service raises for every call that ADDS a source,
    // because a repeat call for an already-active source is not a start — nothing joins and the level
    // does not move — and every raise fans out to AudioManager, which writes an Information line for
    // it.
    var service = CreateService();
    var source = CreateEventSource("event-1");
    await service.StartDuckingAsync(source);

    var raisedAfterFirst = 0;
    service.DuckingStateChanged += (_, _) => raisedAfterFirst++;

    await service.StartDuckingAsync(source);

    Assert.Equal(0, raisedAfterFirst);
    Assert.Equal(1, service.ActiveEventCount);
  }

  [Fact]
  public async Task SetPriority_DoesNotChangeTheDuckLevel_TODAY()
  {
    // Priority currently arbitrates nothing: the duck target is the fixed global
    // Audio:DuckingPercentage regardless of the priorities involved. INTEGRATIONS.md records
    // the same finding in the doc.
    var service = CreateService();
    var low = CreateEventSource("event-low");
    var high = CreateEventSource("event-high");

    service.SetPriority(low, 2);
    await service.StartDuckingAsync(low);
    var levelWithLowPriority = service.CurrentDuckLevel;

    service.SetPriority(high, 10);
    await service.StartDuckingAsync(high);

    Assert.Equal(levelWithLowPriority, service.CurrentDuckLevel);
  }

  [Fact]
  public async Task ActiveEventCount_IsReferenceCounted()
  {
    var service = CreateService();
    var first = CreateEventSource("event-1");
    var second = CreateEventSource("event-2");

    await service.StartDuckingAsync(first);
    await service.StartDuckingAsync(second);
    Assert.Equal(2, service.ActiveEventCount);

    await service.StopDuckingAsync(first);
    Assert.Equal(1, service.ActiveEventCount);
    Assert.True(service.IsDucking);

    await service.StopDuckingAsync(second);
    Assert.Equal(0, service.ActiveEventCount);
    Assert.False(service.IsDucking);
  }

  [Fact]
  public async Task ASourceLeavingWhileOthersRemainStillRaises()
  {
    // ⚠ THIS IS THE PHN-1f CHANGE, characterized. Before it, this scenario produced ZERO raises: the
    // raise lived inside `if (needsRestore)`, and needsRestore is `_isDucking && remaining == 0`. So a
    // priority-8 blocker ending while a priority-3 announcement kept ducking was invisible to every
    // subscriber — which is exactly the source-ended fact D28's queue has to hear in order to wake.
    //
    // MUTATION (§2.1): put the raise back inside `if (needsRestore)` and the count below is 0.
    var service = CreateService();
    var first = CreateEventSource("event-1");
    var second = CreateEventSource("event-2");

    _fixture.Options.DuckingPolicy = DuckingPolicy.Instant;

    await service.StartDuckingAsync(first);
    await service.StartDuckingAsync(second);

    var raised = 0;
    DuckingStateChangedEventArgs? last = null;
    service.DuckingStateChanged += (_, args) => { raised++; last = args; };

    await service.StopDuckingAsync(first);

    Assert.Equal(1, raised);
    Assert.NotNull(last);
    Assert.Equal(DuckingSourceTransition.Ended, last!.Transition);

    // The set did NOT empty, so this is the case the old rule could not express at all.
    Assert.Equal(1, service.ActiveEventCount);
  }
}
