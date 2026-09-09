using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Covers the no-player-registered contract of the transport methods added by ADR-029 PR 1, plus the
/// same contract on <c>StopAsync</c>, which PHN-10 turned from an incidental property into one the
/// production code depends on. The populated-dictionary paths need a real device and are exercised by
/// UAT (see the plan Test Plan §2.2), not here.
/// </summary>
public class SoundFlowPlaybackServiceTransportTests
{
  [Fact]
  public void GetPosition_ReturnsNull_WhenNoPlayerIsRegistered()
  {
    var service = CreateService();

    Assert.Null(service.GetPosition("no-such-source"));
  }

  [Fact]
  public void Seek_ReturnsFalse_WhenNoPlayerIsRegistered()
  {
    var service = CreateService();

    Assert.False(service.Seek("no-such-source", TimeSpan.FromSeconds(5)));
  }

  [Fact]
  public void Seek_ReturnsFalse_ForANegativePositionOnAnUnregisteredSource()
  {
    // Named for what it actually pins. Seek's negative-position guard runs BEFORE the dictionary
    // lookup, so this assertion would still hold with that guard deleted — the lookup alone
    // returns false. Reaching the guard itself needs a registered player, which needs a real
    // device, so it is a UAT case (plan Test Plan §2.2) rather than one this file can carry.
    var service = CreateService();

    Assert.False(service.Seek("no-such-source", TimeSpan.FromSeconds(-1)));
  }

  [Fact]
  public async Task StopAsync_IsANoOp_WhenNoPlayerIsRegistered()
  {
    // ⭐ PHN-10 made AudioFileEventSource call this UNCONDITIONALLY, on every stop and again on
    // every dispose — so "safe on an unknown key" went from incidental to load-bearing. Before that
    // row the _isPlaybackActive guard meant it was never called with a stale key at all.
    //
    // ⚠ It pins a PRECONDITION the fix relies on, and nothing more. It does not exercise the
    // defect, does not stop any audio, and cannot: no player can be registered on a device-free
    // service (see DeviceFreePlaybackService's remarks). Do not cite it as coverage of PHN-10.
    var service = CreateService();

    await service.StopAsync("no-such-source");

    // ⚠ The property is "does not throw", which an empty body would also express — and an empty body
    // is indistinguishable from one that was gutted. Asserting the id is still absent afterwards
    // gives the test something to fail on.
    Assert.False(service.IsPlaying("no-such-source"));
    Assert.Null(service.GetPosition("no-such-source"));
  }

  private static SoundFlowPlaybackService CreateService() => DeviceFreePlaybackService.Create();
}
