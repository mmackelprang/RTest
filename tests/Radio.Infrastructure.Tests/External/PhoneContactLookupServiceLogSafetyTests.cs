using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Bluetooth;
using Radio.Core.Models;
using Radio.Core.Utilities;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

/// <summary>
/// <c>PHN-5</c> <c>T2</c>: drives the REAL <see cref="PhoneContactLookupService"/> down each of its
/// five logging arms (<c>P1</c>–<c>P5</c>) and asserts no raw phone number and no contact name
/// reaches the log.
/// </summary>
/// <remarks>
/// ⚠ <b>Every arm asserts on BOTH the whole sentinel and its last four digits.</b> The line at
/// <c>:90</c> was already "masked" before this row, as <c>$"***{phoneNumber[^4..]}"</c> — so a test
/// that only looked for the whole number would have passed against the pre-fix code while
/// <c>contact.Name</c> leaked in clear beside it. The <c>7424</c> assertion and the NAME assertion
/// are the two that actually catch that line.
///
/// ⚠ <b>Every arm also asserts the captured log is NON-EMPTY and that the expected token is
/// present.</b> Without those two, the whole class passes vacuously against a component that logs
/// nothing at all, which is a green run that proves the opposite of what it appears to.
///
/// The harness is <see cref="CapturingLoggerProvider"/> (defined in <c>GvMediaClientTests.cs</c>,
/// same assembly). Two of its properties are load-bearing here and neither is incidental: its
/// <c>IsEnabled</c> returns <c>true</c> at every level, which is the only reason the Debug arms
/// <c>P2</c>/<c>P3</c>/<c>P4</c> are observable at all; and it records
/// <c>exception.ToString()</c> alongside the formatted message, which is what makes
/// <c>P5</c>'s exception channel observable. <see cref="Harness_ObservesTheExceptionChannel"/>
/// proves the second rather than assuming it — a harness that ignored the exception would report
/// green while the number leaked through a stack trace.
/// </remarks>
public class PhoneContactLookupServiceLogSafetyTests
{
  /// <summary>
  /// Distinctive enough that a "does not contain" assertion cannot pass by accident against
  /// fixture data, and its last four digits are searchable on their own.
  /// </summary>
  private const string Sentinel = "5550137424";

  private const string Last4 = "7424";

  /// <summary>A real contact name, which after PHN-5 must appear in no log line at all.</summary>
  private const string ContactName = "Marmalade Pemberton";

  private static PhoneContactLookupService Build(
    CapturingLoggerProvider capture,
    HttpMessageHandler handler,
    IPbapContactRepository? pbapRepo = null,
    IBluetoothService? bluetooth = null)
  {
    var options = new Mock<IOptionsMonitor<PhoneIntegrationOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new PhoneIntegrationOptions
    {
      // Never reached: every handler below answers without a socket.
      ContactsApiBaseUrl = "http://contacts.invalid"
    });

    return new PhoneContactLookupService(
      capture.CreateLogger<PhoneContactLookupService>(),
      options.Object,
      new HttpClient(handler),
      pbapRepo,
      bluetooth);
  }

  private static void AssertNoPii(CapturingLoggerProvider capture)
  {
    var messages = capture.Messages;

    // ⚠ Guard first: without this the three assertions below are satisfied by an empty list.
    Assert.NotEmpty(messages);

    foreach (var message in messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      // Catches a reinstated "***{last4}" mask, which the whole-number check sails past.
      Assert.DoesNotContain(Last4, message, StringComparison.Ordinal);
      Assert.DoesNotContain(ContactName, message, StringComparison.OrdinalIgnoreCase);
      // The surname on its own, in case only the display form changes shape.
      Assert.DoesNotContain("Pemberton", message, StringComparison.OrdinalIgnoreCase);
    }

    // ⚠ And prove the number was actually logged in its masked form, so this is a test about
    // MASKING rather than about deletion — deleting the argument would also satisfy the above.
    var token = LogSafeText.ForPhone(Sentinel);
    Assert.Contains(messages, m => m.Contains(token, StringComparison.Ordinal));
  }

  [Fact]
  public void Harness_ObservesTheExceptionChannel()
  {
    // ⚠ This test pins the HARNESS, not the component, and it runs first for that reason. Three of
    // this row's sites log an exception, and the request URL embeds the number
    // (…/api/contacts/lookup?phone=…). If CapturingLoggerProvider recorded only the formatted
    // message, every exception-arm test below would report green while the number leaked through
    // exception.ToString(). Falsifying mutation: drop the `sink.Add(exception.ToString())` branch
    // in CapturingLoggerProvider → this fails and the P5 arm silently stops proving anything.
    var capture = new CapturingLoggerProvider();
    var logger = capture.CreateLogger<PhoneContactLookupServiceLogSafetyTests>();

    logger.LogWarning(new InvalidOperationException($"boom {Sentinel}"), "a message with no PII");

    Assert.Contains(capture.Messages, m => m.Contains(Sentinel, StringComparison.Ordinal));
  }

  [Fact]
  public async Task P1_PbapHit_LogsNeitherTheNumberNorTheDisplayName()
  {
    var capture = new CapturingLoggerProvider();

    var repo = new Mock<IPbapContactRepository>();
    repo
      .Setup(r => r.FindByPhoneNumberAsync(
        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(new PbapContact { DisplayName = ContactName });

    var bluetooth = new Mock<IBluetoothService>();
    bluetooth
      .SetupGet(b => b.ConnectedDevice)
      .Returns(new BluetoothDeviceInfo { Address = "78:20:51:F5:FB:A7", Name = "Handset" });

    var service = Build(
      capture, new StubHandler(_ => throw new InvalidOperationException("REST must not be reached")),
      repo.Object, bluetooth.Object);

    var result = await service.FindCallerNameAsync(Sentinel);

    // ⚠ The name is still the METHOD'S RETURN VALUE, and that is the feature. Only the log
    // argument was in scope; a fix that deleted the name from both would break the announcement.
    Assert.Equal(ContactName, result);
    AssertNoPii(capture);
  }

  [Fact]
  public async Task P2_And_P4_NonSuccessResponse_LogNeitherNumber()
  {
    // 404 walks the "looking up" line (P2) and then the status-code line (P4).
    var capture = new CapturingLoggerProvider();
    var service = Build(capture, new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

    var result = await service.FindCallerNameAsync(Sentinel);

    // The documented fallback: the raw number is the RETURN value when nothing resolves.
    Assert.Equal(Sentinel, result);
    AssertNoPii(capture);
    Assert.Contains(capture.Messages, m => m.Contains("NotFound", StringComparison.Ordinal));
  }

  [Fact]
  public async Task P3_RestHit_LogsNeitherTheNumberNorTheName()
  {
    // ⭐ THE ARM THAT MATTERS MOST. Before PHN-5 this line printed "***7424" — so a test asserting
    // only that the whole number is absent would have PASSED against the broken code, while
    // contact.Name went to the log in clear. The name assertion inside AssertNoPii is what fails
    // when :90's raw argument is restored.
    var capture = new CapturingLoggerProvider();
    var service = Build(capture, new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent($"{{\"name\":\"{ContactName}\",\"phoneNumber\":\"{Sentinel}\"}}",
        System.Text.Encoding.UTF8, "application/json")
    }));

    var result = await service.FindCallerNameAsync(Sentinel);

    Assert.Equal(ContactName, result);
    AssertNoPii(capture);
  }

  [Fact]
  public async Task P5_ThrowingTransport_LogsNeitherNumberNorLeaksItThroughTheException()
  {
    // The exception is KEPT (plan C-103) — the stack trace is a Warning line's whole diagnostic
    // value — so this arm proves the number is absent from it rather than deleting it to be safe.
    var capture = new CapturingLoggerProvider();
    var service = Build(capture, new StubHandler(_ => throw new HttpRequestException("connection reset")));

    var result = await service.FindCallerNameAsync(Sentinel);

    Assert.Equal(Sentinel, result);
    AssertNoPii(capture);
    // The exception really did reach the log, so the "no PII" sweep above covered it.
    Assert.Contains(capture.Messages, m => m.Contains("connection reset", StringComparison.Ordinal));
  }

  [Fact]
  public async Task P1_PbapThrows_FallsThroughAndStillLogsNoPii()
  {
    // The :69 warning is the one line in this file PHN-5 deliberately did NOT touch — it carries
    // no number and no name. Pinned so a later "consistency" edit that adds one is caught.
    var capture = new CapturingLoggerProvider();

    var repo = new Mock<IPbapContactRepository>();
    repo
      .Setup(r => r.FindByPhoneNumberAsync(
        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("pbap store offline"));

    var bluetooth = new Mock<IBluetoothService>();
    bluetooth
      .SetupGet(b => b.ConnectedDevice)
      .Returns(new BluetoothDeviceInfo { Address = "78:20:51:F5:FB:A7", Name = "Handset" });

    var service = Build(
      capture, new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)),
      repo.Object, bluetooth.Object);

    await service.FindCallerNameAsync(Sentinel);

    AssertNoPii(capture);
    Assert.Contains(capture.Messages, m => m.Contains("falling through", StringComparison.Ordinal));
  }

  /// <summary>
  /// Answers every request from a delegate, so each arm above is driven offline and the
  /// ContactsApiBaseUrl above is never resolved.
  /// </summary>
  private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) =>
      Task.FromResult(respond(request));
  }
}
