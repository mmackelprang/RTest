using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.API.Tests.TestSupport;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-5: how <see cref="AudioStateUpdateService"/> treats <see cref="CastVolumeChangedEventArgs"/>.
/// An initial-sync read must never write master volume (whose setter persists); an external
/// change on the Cast device still must.
/// </summary>
/// <remarks>
/// The handler is private and reached by reflection, matching
/// <c>AudioStateUpdateServiceTests</c>. The service is never started, so no polling loop or
/// timer is involved — every assertion follows a synchronous call.
/// </remarks>
public class AudioStateUpdateServiceCastVolumeTests
{
  private const float ExistingMasterVolume = 0.75f;

  // The production guard is `Math.Abs(_audioManager.MasterVolume - e.Volume) > 0.01f`, so an
  // arriving value inside that band would make BOTH tests below pass against unfixed code.
  private const float ArrivingVolume = 0.08f;

  [Fact]
  public void AnInitialCastVolumeSync_IsLoggedButNeverAppliedToMasterVolume()
  {
    var audio = new Mock<IAudioManager>();
    audio.SetupGet(a => a.MasterVolume).Returns(ExistingMasterVolume);
    audio.SetupGet(a => a.IsMuted).Returns(false);

    var logs = new CapturingLoggerProvider();
    var svc = CreateServiceWithAudioManager(audio.Object, logs);

    RaiseCastVolumeChanged(svc, ArrivingVolume, muted: true, initial: true);

    audio.VerifySet(a => a.MasterVolume = It.IsAny<float>(), Times.Never);
    audio.VerifySet(a => a.IsMuted = It.IsAny<bool>(), Times.Never);

    // The forensic record must survive the fix. Also guards against the whole handler being
    // short-circuited to a bare `return`, which would satisfy the VerifySet assertions vacuously.
    Assert.Contains(logs.Messages, m => m.Contains("initial: True") && m.Contains("not applied"));
    svc.Dispose();
  }

  [Fact]
  public void AnExternalCastVolumeChange_IsStillAppliedToMasterVolume()
  {
    // The anti-vacuity twin: without it, `if (true) return;` at the top of the handler
    // passes the test above.
    var audio = new Mock<IAudioManager>();
    audio.SetupGet(a => a.MasterVolume).Returns(ExistingMasterVolume);
    audio.SetupGet(a => a.IsMuted).Returns(false);

    var svc = CreateServiceWithAudioManager(audio.Object, new CapturingLoggerProvider());

    RaiseCastVolumeChanged(svc, ArrivingVolume, muted: true, initial: false);

    audio.VerifySet(a => a.MasterVolume = ArrivingVolume, Times.Once);
    audio.VerifySet(a => a.IsMuted = true, Times.Once);
    svc.Dispose();
  }

  // AUD-81, pre-merge review M1: a mute changed on the speaker arrives with whatever level the
  // speaker reported beside it — possibly a quantised echo of a console push. Copying that level
  // moved the console's volume because the speaker was muted.
  [Fact]
  public void AMuteOnlyCastChange_CopiesTheMute_ButNeverTheLevel()
  {
    var audio = new Mock<IAudioManager>();
    audio.SetupGet(a => a.MasterVolume).Returns(ExistingMasterVolume);
    audio.SetupGet(a => a.IsMuted).Returns(false);

    var svc = CreateServiceWithAudioManager(audio.Object, new CapturingLoggerProvider());

    RaiseCastVolumeChanged(svc, ArrivingVolume, muted: true, initial: false, volumeChanged: false);

    audio.VerifySet(a => a.MasterVolume = It.IsAny<float>(), Times.Never);
    audio.VerifySet(a => a.IsMuted = true, Times.Once); // anti-vacuity: the mute still lands
    svc.Dispose();
  }

  // --- helpers ---

  private static void RaiseCastVolumeChanged(
    AudioStateUpdateService svc, float volume, bool muted, bool initial, bool volumeChanged = true)
  {
    var method = typeof(AudioStateUpdateService).GetMethod(
      "OnCastVolumeChanged",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(method);
    method!.Invoke(svc, new object?[]
    {
      null,
      new CastVolumeChangedEventArgs
      {
        Volume = volume, IsMuted = muted, IsInitialSync = initial, VolumeChanged = volumeChanged
      }
    });
  }

  /// <summary>
  /// The audio manager goes in through a POPULATED ServiceCollection, not a constructor
  /// parameter: AudioStateUpdateService resolves it with IServiceProvider.GetService.
  /// </summary>
  private static AudioStateUpdateService CreateServiceWithAudioManager(
    IAudioManager audioManager, CapturingLoggerProvider logs)
  {
    var hubContextMock = new Mock<IHubContext<AudioStateHub>>();
    var clientsMock = new Mock<IHubClients>();
    var allClientsMock = new Mock<IClientProxy>();
    allClientsMock
      .Setup(c => c.SendCoreAsync(
        It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);
    clientsMock.SetupGet(c => c.All).Returns(allClientsMock.Object);
    hubContextMock.SetupGet(h => h.Clients).Returns(clientsMock.Object);

    var services = new ServiceCollection();
    services.AddSingleton(audioManager);

    return new AudioStateUpdateService(
      logs.CreateLogger<AudioStateUpdateService>(),
      hubContextMock.Object,
      services.BuildServiceProvider(),
      new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
  }
}
