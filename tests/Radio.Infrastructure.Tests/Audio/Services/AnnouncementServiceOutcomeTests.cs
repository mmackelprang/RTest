using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.Audio;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// TTS-2: <see cref="AnnouncementService.AnnounceAsync"/> reports how the announcement ended instead
/// of returning the same thing for success and for a swallowed failure.
/// </summary>
/// <remarks>
/// Each test asserts a specific outcome, and the success case is in the set, so a service that
/// hard-coded any single value fails at least two of them.
/// </remarks>
public class AnnouncementServiceOutcomeTests
{
  [Fact]
  public async Task ATtsFactoryFailureIsReportedAsFailed()
  {
    // TTS-1's chain: Google rejected the voice, TTSFactory threw, AnnounceAsync swallowed it.
    var factory = new Mock<ITTSFactory>();
    factory
      .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("Google TTS API error: 400"));

    var outcome = await CreateService(factory).AnnounceAsync("hello");

    Assert.Equal(AnnouncementOutcome.Failed, outcome);
  }

  [Fact]
  public async Task ARealTtsSourceThatCannotReachAPlaybackDeviceIsReportedAsFailed()
  {
    // End to end through the real TTSEventSource: with no playback device it raises
    // PlaybackCompleted(Error) synchronously inside PlayAsync. Before TTS-2 any completion counted
    // as success.
    var tts = new TTSEventSource(
      "hello", new TTSParameters(), new MemoryStream(new byte[1000]), TimeSpan.FromSeconds(1),
      Mock.Of<ILogger<TTSEventSource>>(), DeviceLessPlaybackService.Create());

    var outcome = await CreateService(FactoryReturning(tts)).AnnounceAsync("hello");

    Assert.Equal(AnnouncementOutcome.Failed, outcome);
  }

  [Theory]
  [InlineData(PlaybackCompletionReason.EndOfContent, AnnouncementOutcome.Completed)]
  [InlineData(PlaybackCompletionReason.Error, AnnouncementOutcome.Failed)]
  [InlineData(PlaybackCompletionReason.UserStopped, AnnouncementOutcome.Interrupted)]
  public async Task TheCompletionReasonDecidesTheOutcome(
    PlaybackCompletionReason reason, AnnouncementOutcome expected)
  {
    var source = new Mock<IEventAudioSource>();
    source.SetupGet(s => s.Id).Returns("evt-test");
    source.SetupGet(s => s.Name).Returns("TTS event (mocked)");
    source
      .Setup(s => s.PlayAsync(It.IsAny<CancellationToken>()))
      .Returns(() =>
      {
        source.Raise(s => s.PlaybackCompleted += null, source.Object,
          new AudioSourceCompletedEventArgs { SourceId = "evt-test", Reason = reason });
        return Task.CompletedTask;
      });

    var outcome = await CreateService(FactoryReturning(source.Object)).AnnounceAsync("hello");

    Assert.Equal(expected, outcome);
  }

  [Fact]
  public void AnUnassignedOutcomeReadsAsFailureNotSuccess()
  {
    // The enum's zero value is Failed on purpose: an unconfigured double or a default must never
    // report success for something that did not happen.
    Assert.Equal(AnnouncementOutcome.Failed, default(AnnouncementOutcome));
  }

  private static Mock<ITTSFactory> FactoryReturning(IEventAudioSource source)
  {
    var factory = new Mock<ITTSFactory>();
    factory
      .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(source);
    return factory;
  }

  private static AnnouncementService CreateService(Mock<ITTSFactory> factory)
  {
    var options = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new FilePlayerOptions());
    var fileFactory = new AudioFileEventSourceFactory(
      Mock.Of<ILogger<AudioFileEventSourceFactory>>(),
      Mock.Of<ILogger<AudioFileEventSource>>(),
      options.Object);

    return new AnnouncementService(
      Mock.Of<ILogger<AnnouncementService>>(),
      factory.Object,
      Mock.Of<IDuckingService>(),
      fileFactory);
  }
}
