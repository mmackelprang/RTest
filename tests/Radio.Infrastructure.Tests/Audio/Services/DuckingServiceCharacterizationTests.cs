using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// CHARACTERIZATION tests: these assert what DuckingService does TODAY, not what it should do.
///
/// They exist for ADR-029 D5 / PHN arc PR 4, which makes priority load-bearing for the first
/// time in this system. Today ducking is binary and reference-counted: the first event fades the
/// primary to the fixed global Audio:DuckingPercentage and every subsequent concurrent event
/// changes nothing. PR 4 must change the second assertion below - and when it does, the change
/// appears as an edited test in PR 4's own diff rather than as a silent behavioural shift inside
/// a shared audio service.
///
/// ⚠ If you are reading this in PR 4: update these, do not delete them.
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
  public async Task StartDuckingAsync_DoesNotRaise_ForASecondConcurrentEvent_TODAY()
  {
    // ⚠ ADR-029 §6.3 requires this to become 2. That is PR 4's change, and this assertion is
    // where it becomes visible. DuckingService computes needsTransition = !_isDucking, and the
    // raise sits inside if (needsTransition); the second event reaches only a LogDebug.
    var service = CreateService();
    await service.StartDuckingAsync(CreateEventSource("event-1"));

    var raisedAfterFirst = 0;
    service.DuckingStateChanged += (_, _) => raisedAfterFirst++;

    await service.StartDuckingAsync(CreateEventSource("event-2"));

    Assert.Equal(0, raisedAfterFirst);
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
}
