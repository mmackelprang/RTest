using Microsoft.Extensions.Time.Testing;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-74: a release that a new duck episode cancels mid-fade must not announce ducking as ended.
/// AudioManager treats <c>IsDucking:false</c> as "restore full volume", so that raise swelled the
/// radio back up while the new announcement's attack was still ramping down.
/// </summary>
/// <remarks>
/// Deterministic, not timed: the service's fade-step delays run on a <see cref="FakeTimeProvider"/>
/// that the test never advances, so a fade that has taken its first step is parked on its first
/// delay for as long as the test likes. The release is therefore provably mid-fade when the second
/// start arrives — no wall clock is raced (CLAUDE.md § Test Timing).
/// </remarks>
public class DuckingSupersededReleaseTests
{
  private readonly DuckingServiceFixture _fixture = new();
  private readonly FakeTimeProvider _time = new();

  [Fact]
  public async Task AReleaseCancelledByANewEpisode_DoesNotAnnounceDuckingEnded()
  {
    using DuckingService service = _fixture.CreateService(_time);
    List<DuckingStateChangedEventArgs> raises = [];
    service.DuckingStateChanged += (_, e) =>
    {
      lock (raises)
      {
        raises.Add(e);
      }
    };
    IEventAudioSource first = _fixture.CreateEventSource("first");
    IEventAudioSource second = _fixture.CreateEventSource("second");

    // Instant attack for the first duck so it completes without the clock.
    _fixture.Options.DuckingAttackMs = 0;
    await service.StartDuckingAsync(first);
    _fixture.Options.DuckingAttackMs = 100;

    // The release takes its first step and parks on the frozen clock.
    Task release = service.StopDuckingAsync(first);
    Assert.False(release.IsCompleted);
    Assert.False(service.IsDucking);

    // A new announcement: its attack cancels the release fade, takes one step, and parks.
    Task attack = service.StartDuckingAsync(second);
    Assert.False(attack.IsCompleted);

    await release;

    DuckingStateChangedEventArgs ended;
    lock (raises)
    {
      ended = Assert.Single(raises, e => e.Transition == DuckingSourceTransition.Ended);
      Assert.DoesNotContain(raises, e => !e.IsDucking);
    }
    Assert.Same(first, ended.TriggeringSource);
    Assert.True(ended.IsDucking);
    Assert.True(service.IsDucking);

    // Tidy: Dispose cancels the parked attack fade so its task completes.
    service.Dispose();
    await attack;
  }

  [Fact]
  public async Task AReleaseThatIsNotSuperseded_StillAnnouncesDuckingEnded()
  {
    // Guards the fix from over-reaching: an ordinary release must still end ducking.
    using DuckingService service = _fixture.CreateService(_time);
    List<DuckingStateChangedEventArgs> raises = [];
    service.DuckingStateChanged += (_, e) => raises.Add(e);
    IEventAudioSource only = _fixture.CreateEventSource("only");

    _fixture.Options.DuckingPolicy = DuckingPolicy.Instant;
    await service.StartDuckingAsync(only);
    await service.StopDuckingAsync(only);

    DuckingStateChangedEventArgs ended = Assert.Single(raises, e => e.Transition == DuckingSourceTransition.Ended);
    Assert.False(ended.IsDucking);
    Assert.False(service.IsDucking);
  }
}
