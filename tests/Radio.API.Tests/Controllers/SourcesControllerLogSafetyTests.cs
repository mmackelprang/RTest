using Microsoft.AspNetCore.Mvc;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.API.Tests.TestSupport;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Utilities;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// <c>TTS-11</c> <c>T6</c> (TTS-event half): <see cref="SourcesController"/>'s TTS event route
/// writes no utterance to the log.
/// </summary>
/// <remarks>
/// The <see cref="ITTSFactory"/> is a mock here and that is the right call for THIS test: the
/// subject is the controller's own log line, which fires before the factory is called at all. The
/// real factory's own line is pinned separately and directly by
/// <c>TTSFactoryLogSafetyTests</c> in <c>Radio.Infrastructure.Tests</c> — which is how the
/// property gets covered without a fake standing in for the thing under test.
///
/// ⚠ The mocked source's <c>Name</c> is set to a constant DELIBERATELY. A real
/// <c>TTSEventSource</c> here would embed the utterance in its name and the mixer's bookkeeping
/// line would then decide the outcome — moving the subject of the test from this controller to
/// <c>SoundFlowMasterMixer</c>, which has its own pin.
/// </remarks>
public class SourcesControllerLogSafetyTests
{
  /// <summary>
  /// Chosen to be absent from every other fixture in the suite, so a <c>DoesNotContain</c> cannot
  /// pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  private static async Task<CapturingLoggerProvider> PlayTtsAsync()
  {
    var logs = new CapturingLoggerProvider();

    var ttsSource = new Mock<IEventAudioSource>();
    ttsSource.SetupGet(s => s.Name).Returns("TTS event (mocked)");
    ttsSource.SetupGet(s => s.Id).Returns("evt-test");

    var factory = new Mock<ITTSFactory>();
    factory
      .Setup(f => f.CreateAsync(It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(ttsSource.Object);

    var engine = new Mock<IAudioEngine>();
    engine.Setup(e => e.GetMasterMixer()).Returns(Mock.Of<IMasterMixer>());

    var controller = new SourcesController(
      logs.CreateLogger<SourcesController>(), engine.Object, ttsFactory: factory.Object);

    var result = await controller.PlayTTSEvent(
      new PlayTTSRequest { Text = Sentinel, Engine = "Google" }, CancellationToken.None);

    // The route really ran to completion: a BadRequest or a 501 would skip the log line and make
    // every assertion below vacuous in a way "no sentinel in the log" cannot distinguish.
    Assert.IsType<OkObjectResult>(result);
    factory.Verify(
      f => f.CreateAsync(Sentinel, It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()),
      Times.Once);

    return logs;
  }

  [Fact]
  public async Task NoLogLineFromATtsEventRequestCarriesTheUtterance()
  {
    var logs = await PlayTtsAsync();

    // ⚠ Without this the whole test passes vacuously against a controller that logs nothing.
    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      Assert.DoesNotContain("Marmalade", message, StringComparison.Ordinal);
    }
  }

  [Fact]
  public async Task TheTtsEventLineKeepsTheTokenAndTheEngine()
  {
    // "No text" must not be achieved by logging nothing: the engine is what an operator reads to
    // answer "which engine was asked", and the token joins this line to the factory's own.
    var logs = await PlayTtsAsync();

    var line = Assert.Single(
      logs.Messages, m => m.StartsWith("Playing TTS event:", StringComparison.Ordinal));

    Assert.Contains(LogSafeText.For(Sentinel), line, StringComparison.Ordinal);
    Assert.Contains("Google", line, StringComparison.Ordinal);
  }
}
