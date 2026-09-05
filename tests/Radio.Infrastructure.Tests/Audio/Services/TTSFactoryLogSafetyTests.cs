using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Utilities;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// <c>TTS-11</c> <c>T2</c>: the real <see cref="TTSFactory"/>'s "creating TTS audio" line carries
/// no utterance text.
/// </summary>
/// <remarks>
/// <b>How this reaches the line offline, since the plan flagged it as the one blocking unknown.</b>
/// <c>CreateAsync</c> runs, in order: a null/whitespace guard on the text, engine parsing, the
/// voice guard, THEN the log line, THEN per-engine generation. The secrets monitor is not read
/// until generation. So a factory with a valid engine and a configured voice but no API key logs
/// the line and then throws <c>InvalidOperationException</c> from
/// <c>GenerateGoogleTTSAsync</c>'s own key check — before it constructs an <c>HttpClient</c>, so
/// there is no network call, no timeout and nothing flaky. The throw is expected and caught here.
///
/// ⚠ Both guards ahead of the line are load-bearing for this test and are set deliberately:
/// <c>DefaultVoice</c> must be non-empty or the voice guard throws first, and
/// <c>DefaultEngine</c> must be a recognised name or <c>ParseEngine</c> throws first. Either would
/// make the assertions below unreachable rather than merely wrong.
///
/// ⚠ <b>WHAT THIS FILE CANNOT COVER, stated rather than hidden behind a confident name.</b>
/// Anything after the engine call. <see cref="TTSFactory"/> constructs its <c>HttpClient</c>
/// inline rather than taking one by injection, so a SUCCESSFUL synthesis cannot be simulated
/// offline. The lines that log byte counts and durations on that path were read and carry no text,
/// but nothing here pins them. That is a real hole; closing it means making the HttpClient
/// injectable, which is a DI change to a live shared service and not a logging fix.
/// </remarks>
public class TTSFactoryLogSafetyTests
{
  /// <summary>
  /// Chosen to be absent from every other fixture in the suite, so a <c>DoesNotContain</c> cannot
  /// pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  private static async Task<CapturingLoggerProvider> CreateAndExpectFailureAsync()
  {
    var logs = new CapturingLoggerProvider();

    var options = new Mock<IOptionsMonitor<TTSOptions>>();
    options.Setup(o => o.CurrentValue).Returns(new TTSOptions
    {
      DefaultEngine = "Google",
      DefaultVoice = "en-US-Standard-A",
      DefaultSpeed = 1.0f,
      DefaultPitch = 1.0f
    });

    // No API key. This is what makes generation fail immediately, and it fails before any
    // HttpClient is constructed.
    var secrets = new Mock<IOptionsMonitor<TTSSecrets>>();
    secrets.Setup(s => s.CurrentValue).Returns(new TTSSecrets());

    using var factory = new TTSFactory(
      logs.CreateLogger<TTSFactory>(),
      logs.CreateLogger<TTSEventSource>(),
      options.Object,
      secrets.Object);

    var thrown = await Record.ExceptionAsync(() => factory.CreateAsync(Sentinel));

    // The failure is the expected one, not some earlier guard that skipped the log line. Without
    // this the test could be passing because CreateAsync threw before it ever logged.
    var invalid = Assert.IsType<InvalidOperationException>(thrown);
    Assert.Contains("API key", invalid.Message, StringComparison.OrdinalIgnoreCase);

    return logs;
  }

  [Fact]
  public async Task TheCreateLineCarriesTheTokenAndNotTheUtterance()
  {
    var logs = await CreateAndExpectFailureAsync();

    // ⚠ Without this the whole test passes vacuously against a factory that logs nothing at all.
    Assert.NotEmpty(logs.Messages);

    var created = Assert.Single(
      logs.Messages, m => m.StartsWith("Creating TTS audio for text:", StringComparison.Ordinal));

    Assert.DoesNotContain(Sentinel, created, StringComparison.Ordinal);
    Assert.DoesNotContain("Marmalade", created, StringComparison.Ordinal);

    // …and "no text" is not achieved by logging nothing: the token is what joins this line forward
    // to TTSEventSource's own line for the same utterance.
    Assert.Contains(LogSafeText.For(Sentinel), created, StringComparison.Ordinal);
    Assert.Contains("Google", created, StringComparison.Ordinal);
  }

  [Fact]
  public async Task NoLineWrittenOnTheFailedCreatePathCarriesTheUtterance()
  {
    // Every captured line, including the exception's own ToString() which the capturing logger
    // records as a separate entry — an utterance embedded in an exception message would reach the
    // sink exactly as a log argument would.
    var logs = await CreateAndExpectFailureAsync();

    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      Assert.DoesNotContain("Marmalade", message, StringComparison.Ordinal);
    }
  }
}
