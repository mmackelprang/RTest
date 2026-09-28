using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting.Services;
using Radio.Fingerprinting.Tests.TestSupport;

namespace Radio.Fingerprinting.Tests.Services;

/// <summary>
/// AUD-33: the two facts about a real identification cycle that the sources' stale-result drop depends
/// on, pinned through the service's own raise site rather than hand-built event args.
/// </summary>
/// <remarks>
/// Runs one real <c>ExecuteAsync</c> cycle against <see cref="MockAudioSampleProvider"/> and a mocked
/// SongRec. It waits on the <see cref="BackgroundIdentificationService.TrackIdentified"/> event itself —
/// no sleep races a production timer — but the service's fixed 5 s start-up delay makes it a ~5 s test.
/// </remarks>
public class BackgroundIdentificationServiceCaptureTimeTests
{
  [Fact]
  public async Task RaisedIdentification_CarriesItsCaptureStartTime_AndIsAlreadyMarkedAsRecent()
  {
    var tap = new MockAudioSampleProvider();
    tap.SetActive(true, "Test", PlaySource.File);

    var track = new TrackMetadata
    {
      Id = "t1",
      Title = "Meditating Beat",
      Artist = "Kevin MacLeod",
      Source = MetadataSource.Shazam,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    var songRec = new Mock<ISongRecRecognitionService>();
    songRec.SetupGet(s => s.IsAvailable).Returns(true);
    songRec.Setup(s => s.RecognizeAsync(It.IsAny<AudioSampleBuffer>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(track);

    var services = new ServiceCollection();
    services.AddSingleton<IAudioSampleProvider>(tap);
    services.AddSingleton(songRec.Object);
    using var provider = services.BuildServiceProvider();

    var options = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    options.Setup(o => o.CurrentValue).Returns(new FingerprintingOptions
    {
      Enabled = true,
      SampleDurationSeconds = 1
    });

    using var service = new BackgroundIdentificationService(
      new Mock<ILogger<BackgroundIdentificationService>>().Object, provider, options.Object);

    var raised = new TaskCompletionSource<(TrackIdentifiedEventArgs Args, bool MarkedBeforeRaise)>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    service.TrackIdentified += (_, e) =>
      raised.TrySetResult((e, service.IsSuppressedAsDuplicateForTesting(e.Track)));

    var beforeStart = DateTime.UtcNow;
    await service.StartAsync(CancellationToken.None);
    try
    {
      // A safety net only: the rendezvous is the event, not the clock.
      var (args, markedBeforeRaise) = await raised.Task.WaitAsync(TimeSpan.FromSeconds(60));

      // Without this, WasCapturedBefore fails open and every source accepts every stale result.
      Assert.NotNull(args.CaptureStartedAt);
      Assert.InRange(args.CaptureStartedAt!.Value, beforeStart, args.IdentifiedAt);

      // ForgetRecentIdentification exists because of this ordering: the result is suppression-marked
      // before any source sees it. If that ever changes, the forget call becomes dead code.
      Assert.True(markedBeforeRaise);
    }
    finally
    {
      await service.StopAsync(CancellationToken.None);
    }
  }
}
