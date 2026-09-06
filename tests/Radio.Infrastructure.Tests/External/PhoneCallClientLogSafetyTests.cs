using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.External;
using Radio.Core.Utilities;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

/// <summary>
/// <c>PHN-5</c> <c>T2</c>: pins <c>P6</c>, <see cref="PhoneCallClient"/>'s call-state line, which
/// logged a raw phone number AND a real contact's display name at Information.
/// </summary>
/// <remarks>
/// ⭐ <b>This drives the REAL handler, not a copy of it.</b> The plan left open whether the site
/// was reachable without a live SignalR hub — it is reached through
/// <c>_hubConnection.On&lt;string, string, string&gt;("CallStateChanged", …)</c> — and the answer
/// taken here is to widen <c>OnCallStateChangedWithName</c> from <c>private</c> to
/// <c>internal</c>, which <c>Radio.Infrastructure.csproj</c>'s existing <c>InternalsVisibleTo</c>
/// already exposes to this assembly. The registration is a method group, so the live path and the
/// path these tests drive are one method. The alternative — a test that reimplements the handler —
/// would pin a copy and stay green while production leaked, which is the exact failure mode this
/// repository has shipped before.
///
/// <see cref="PhoneCallClient.StartAsync"/> is never called: it would build a real
/// <c>HubConnection</c> and attempt a socket. Nothing in these tests needs it, because the handler
/// does not read the connection.
/// </remarks>
public class PhoneCallClientLogSafetyTests
{
  private const string Sentinel = "5550137424";
  private const string Last4 = "7424";
  private const string ContactName = "Marmalade Pemberton";

  private static PhoneCallClient Build(CapturingLoggerProvider capture)
  {
    var options = new Mock<IOptionsMonitor<PhoneIntegrationOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new PhoneIntegrationOptions());

    return new PhoneCallClient(capture.CreateLogger<PhoneCallClient>(), options.Object);
  }

  [Theory]
  [InlineData(ContactName)]
  [InlineData(null)]
  public void P6_CallStateLine_CarriesNeitherTheNumberNorTheName(string? callerName)
  {
    var capture = new CapturingLoggerProvider();
    var client = Build(capture);

    client.OnCallStateChangedWithName("ringing", Sentinel, callerName);

    var messages = capture.Messages;
    // ⚠ Guard first: every assertion below is satisfied by an empty list.
    Assert.NotEmpty(messages);

    foreach (var message in messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      // Catches a reinstated "***{last4}" mask, which the whole-number check sails past.
      Assert.DoesNotContain(Last4, message, StringComparison.Ordinal);
      Assert.DoesNotContain(ContactName, message, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("Pemberton", message, StringComparison.OrdinalIgnoreCase);
    }

    // Masked, not deleted — deletion would satisfy the sweep above just as well.
    Assert.Contains(messages, m => m.Contains(LogSafeText.ForPhone(Sentinel), StringComparison.Ordinal));
  }

  [Fact]
  public void P6_NameResolvedBoolean_ReportsWhetherANameResolved()
  {
    // §1.3's replacement for the deleted name: at this site the useful fact is not WHICH name came
    // back but whether one did — that is what separates "PBAP is working" from "PBAP returned
    // nothing and we announced a phone number". If the boolean did not track callerName the field
    // would be decoration, so both arms are asserted.
    var withName = new CapturingLoggerProvider();
    Build(withName).OnCallStateChangedWithName("ringing", Sentinel, ContactName);
    Assert.Contains(withName.Messages, m => m.Contains("NameResolved: True", StringComparison.Ordinal));

    var withoutName = new CapturingLoggerProvider();
    Build(withoutName).OnCallStateChangedWithName("ringing", Sentinel, null);
    Assert.Contains(withoutName.Messages, m => m.Contains("NameResolved: False", StringComparison.Ordinal));
  }

  [Fact]
  public void P6_TheRawNumberAndNameStillReachSubscribers()
  {
    // ⚠ The cached state and the event payload are the FEATURE — the UI displays both — so this
    // test exists to make a future over-eager "mask everything" edit fail loudly. PHN-5 masks what
    // is written to a sink that persists, not what is handed to a subscriber.
    var capture = new CapturingLoggerProvider();
    var client = Build(capture);

    PhoneCallStateChangedEventArgs? received = null;
    client.CallStateChanged += (_, e) => received = e;

    client.OnCallStateChangedWithName("ringing", Sentinel, ContactName);

    Assert.NotNull(received);
    Assert.Equal(Sentinel, received!.PhoneNumber);
    Assert.Equal(ContactName, received.CallerName);
    Assert.Equal(Sentinel, client.CallerNumber);
    Assert.Equal(ContactName, client.CallerName);
  }
}
