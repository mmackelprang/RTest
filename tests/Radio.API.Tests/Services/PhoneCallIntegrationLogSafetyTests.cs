using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.API.Tests.TestSupport;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.External;
using Radio.Core.Utilities;
using Radio.Infrastructure.External;

namespace Radio.API.Tests.Services;

/// <summary>
/// <c>PHN-5</c>: pins <c>P7</c>, <see cref="PhoneCallIntegrationService"/>'s incoming-call line,
/// which logged a raw phone number AND a real contact's display name at Information.
/// </summary>
/// <remarks>
/// ⭐ <b>This test exists because the lint cannot cover this site, and the lint alone was very
/// nearly shipped as if it could.</b> Two distinct gaps meet here:
///
/// <list type="number">
/// <item><c>P7</c> passes <c>e.PhoneNumber</c>, the PascalCase PROPERTY spelling. Every rule in
/// <c>LogSafetyLintTests</c> is built with <c>RegexOptions.Compiled</c> and never
/// <c>IgnoreCase</c>, so the <c>phoneNumber</c> rule never reached this file. That half is now
/// covered by a <c>PhoneNumber</c> rule added beside it in review.</item>
/// <item>The ORIGINAL leak was <c>LogInformation("Phone ringing: {Announcement}", announcement)</c>
/// — and <c>announcement</c> is <c>$"Incoming call from {callerName}"</c>, where
/// <c>callerName</c> falls back to the RAW PHONE NUMBER when no contact resolves. A revert to that
/// shape carries both a number and a name into the journal under an identifier spelled
/// <c>announcement</c>. No rule can key on that word, any more than one can key on <c>Name</c>.
/// <b>Only a behavioural test sees it, which is why this file exists rather than just the
/// rule.</b></item>
/// </list>
///
/// ⭐ <b>It drives the REAL handler, not a copy.</b> <c>HandleIncomingCallAsync</c> was widened from
/// <c>private</c> to <c>internal</c> for this — the same seam idiom, for the same reason, as
/// <c>PhoneCallClient.OnCallStateChangedWithName</c> and <c>PhoneHubService.RaiseIncomingCallForTest</c>
/// in this row. <see cref="PhoneCallIntegrationService.ExecuteAsync"/> is never started: it would
/// open a real SignalR connection, and nothing here needs one.
///
/// ⚠ <c>PhoneIntegrationOptions.Enabled</c> is deliberately left at its <c>false</c> default. That
/// gate lives in <c>ExecuteAsync</c>, not in the handler, so setting it would imply a dependency
/// this test does not have. <c>PlayRingSound</c> IS set — to <c>false</c>, which routes straight to
/// <see cref="IAnnouncementService.AnnounceAsync"/> without a <c>File.Exists</c> probe, so the test
/// touches no filesystem and has one deterministic arm.
/// </remarks>
public class PhoneCallIntegrationLogSafetyTests
{
  private const string Sentinel = "5550137424";
  private const string Last4 = "7424";
  private const string ContactName = "Marmalade Pemberton";
  private const string Announcement = $"Incoming call from {ContactName}";

  /// <summary>
  /// Builds the service with every collaborator stubbed, and hands back the announcement spy.
  /// </summary>
  /// <remarks>
  /// <paramref name="spokenTo"/> captures what is actually handed to the TTS path, which is what
  /// <see cref="P7_TheAnnouncementSpokenAloudStillCarriesTheRealName"/> asserts on. The mock
  /// returns a completed task rather than relying on Moq's default: the handler awaits it inside a
  /// <c>try</c> that would otherwise swallow a null-task NRE into an Error log, turning a broken
  /// arrangement into a green run.
  /// </remarks>
  private static PhoneCallIntegrationService Build(
    CapturingLoggerProvider capture,
    out List<string> spokenTo,
    out Mock<IPhoneIntegrationService> phoneClient)
  {
    var spoken = new List<string>();
    spokenTo = spoken;

    var announcements = new Mock<IAnnouncementService>();
    announcements
      .Setup(a => a.AnnounceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .Callback<string, int, CancellationToken>((message, _, _) => spoken.Add(message))
      .Returns(Task.CompletedTask);
    announcements
      .Setup(a => a.PlaySoundWithAnnouncementAsync(
        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .Callback<string, string, int, CancellationToken>((_, message, _, _) => spoken.Add(message))
      .Returns(Task.CompletedTask);

    phoneClient = new Mock<IPhoneIntegrationService>();
    phoneClient
      .Setup(p => p.ReportCallerResolvedAsync(
        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var options = new Mock<IOptionsMonitor<PhoneIntegrationOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new PhoneIntegrationOptions());

    var contactLookup = new PhoneContactLookupService(
      capture.CreateLogger<PhoneContactLookupService>(), options.Object, OfflineClient());

    return new PhoneCallIntegrationService(
      capture.CreateLogger<PhoneCallIntegrationService>(),
      phoneClient.Object,
      contactLookup,
      announcements.Object,
      // Never touched: BroadcastPhoneStateAsync is called from OnCallStateChanged, one level ABOVE
      // the handler these tests drive. A bare mock is therefore sufficient AND honest — setting up
      // Clients.All would imply a call path this test does not exercise.
      new Mock<IHubContext<AudioStateHub>>().Object,
      Options.Create(new PhoneIntegrationOptions { PlayRingSound = false }));
  }

  /// <summary>
  /// An <see cref="HttpClient"/> whose every request throws, so a lookup can never reach the
  /// network.
  /// </summary>
  /// <remarks>
  /// It throws rather than returning an empty 200 on purpose. Both tests supply a
  /// <c>CallerName</c>, so <c>FindCallerNameAsync</c> is never called and this handler never runs —
  /// but if a future edit makes the handler resolve names unconditionally, a throw fails loudly
  /// where a canned response would let the test pass while quietly depending on a stub.
  /// </remarks>
  private static HttpClient OfflineClient() =>
    new(new ThrowingHandler()) { BaseAddress = new Uri("http://unused.invalid") };

  private sealed class ThrowingHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) =>
      throw new HttpRequestException(
        "PhoneCallIntegrationLogSafetyTests must not reach the network.");
  }

  private static PhoneCallStateChangedEventArgs Ringing() => new()
  {
    State = PhoneCallState.Ringing,
    PhoneNumber = Sentinel,
    CallerName = ContactName
  };

  [Fact]
  public async Task P7_IncomingCall_LogsNeitherTheRawNumberNorTheCallerName()
  {
    var capture = new CapturingLoggerProvider();
    var service = Build(capture, out _, out _);

    await service.HandleIncomingCallAsync(Ringing());

    var messages = capture.Messages;
    // ⚠ Guard first: every assertion below is satisfied by an empty list, and a handler that
    // logged nothing at all would otherwise pass this test perfectly.
    Assert.NotEmpty(messages);

    foreach (var message in messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      // Catches a reinstated "***{last4}" mask, which the whole-number check sails past.
      Assert.DoesNotContain(Last4, message, StringComparison.Ordinal);
      // ⭐ THE ASSERTION THIS FILE EXISTS FOR. A revert to the original
      // `LogInformation("Phone ringing: {Announcement}", announcement)` puts the contact name —
      // and, when no contact resolves, the raw number — straight into the journal. No lint rule
      // can see that shape; this line can.
      Assert.DoesNotContain(ContactName, message, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("Pemberton", message, StringComparison.OrdinalIgnoreCase);
    }

    // Masked, not deleted — deleting both arguments would satisfy every sweep above just as well.
    Assert.Contains(messages,
      m => m.Contains(LogSafeText.ForPhone(Sentinel), StringComparison.Ordinal));
    Assert.Contains(messages,
      m => m.Contains(LogSafeText.For(Announcement), StringComparison.Ordinal));
  }

  [Fact]
  public async Task P7_TheAnnouncementSpokenAloudStillCarriesTheRealName()
  {
    // ⚠ The spoken announcement and the name reported back to RotaryPhone are the FEATURE — the
    // radio says the caller's name out loud, and the phone's UI shows it — so this test exists to
    // make a future over-eager "mask everything" edit fail loudly. PHN-5 masks what is written to a
    // sink that persists, not what is spoken or handed to a collaborator. The sibling guards are
    // P6_TheRawNumberAndNameStillReachSubscribers and
    // P8_IncomingCall_LogsNoRawNumberButStillRaisesItToSubscribers.
    var capture = new CapturingLoggerProvider();
    var service = Build(capture, out var spokenTo, out var phoneClient);

    await service.HandleIncomingCallAsync(Ringing());

    Assert.Equal([Announcement], spokenTo);
    phoneClient.Verify(
      p => p.ReportCallerResolvedAsync(Sentinel, ContactName, It.IsAny<CancellationToken>()),
      Times.Once);
  }
}
