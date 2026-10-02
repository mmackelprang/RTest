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
/// ⭐ <b>This drives the REAL handlers, not copies of them.</b> They are <c>internal</c>, which
/// <c>Radio.Infrastructure.csproj</c>'s <c>InternalsVisibleTo</c> exposes to this assembly, and the live
/// <c>_hubConnection.On&lt;string, string&gt;(…)</c> registrations are method groups, so the live path
/// and the path these tests drive are one method.
///
/// <para>
/// <b>PHN-12 rewired this site</b> to RotaryPhone's real contract: the number now arrives through
/// <c>IncomingCall(phoneId, number)</c> (<see cref="PhoneCallClient.OnHubIncomingCall"/>), and no contact
/// name arrives at all — the three-argument <c>CallStateChanged</c> that carried one was never sent. The
/// name-related arms of this file went with it; the number arm is unchanged in intent.
/// </para>
///
/// <see cref="PhoneCallClient.StartAsync"/> is never called: it would build a real
/// <c>HubConnection</c> and attempt a socket. Nothing in these tests needs it, because the handlers do
/// not read the connection.
/// </remarks>
public class PhoneCallClientLogSafetyTests
{
  private const string Sentinel = "5550137424";
  private const string Last4 = "7424";

  private static PhoneCallClient Build(CapturingLoggerProvider capture)
  {
    var options = new Mock<IOptionsMonitor<PhoneIntegrationOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new PhoneIntegrationOptions());

    return new PhoneCallClient(capture.CreateLogger<PhoneCallClient>(), options.Object);
  }

  [Fact]
  public void P6_TheRingingLine_DoesNotCarryTheNumber()
  {
    var capture = new CapturingLoggerProvider();
    var client = Build(capture);

    client.OnHubCallStateChanged("default", "Ringing");
    client.OnHubIncomingCall("default", Sentinel);
    client.OnHubCallStateChanged("default", "Idle");

    var messages = capture.Messages;
    // ⚠ Guard first: every assertion below is satisfied by an empty list.
    Assert.NotEmpty(messages);

    foreach (var message in messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      // Catches a reinstated "***{last4}" mask, which the whole-number check sails past.
      Assert.DoesNotContain(Last4, message, StringComparison.Ordinal);
    }

    // Masked, not deleted — deletion would satisfy the sweep above just as well.
    Assert.Contains(messages, m => m.Contains(LogSafeText.ForPhone(Sentinel), StringComparison.Ordinal));
  }

  [Fact]
  public void P6_TheRawNumberStillReachesSubscribers()
  {
    // ⚠ The cached state and the event payload are the FEATURE — the announcement speaks the caller —
    // so this test exists to make a future over-eager "mask everything" edit fail loudly. PHN-5 masks
    // what is written to a sink that persists, not what is handed to a subscriber.
    var capture = new CapturingLoggerProvider();
    var client = Build(capture);

    PhoneCallStateChangedEventArgs? received = null;
    client.CallStateChanged += (_, e) => received = e;

    client.OnHubIncomingCall("default", Sentinel);

    Assert.NotNull(received);
    Assert.Equal(PhoneCallState.Ringing, received!.State);
    Assert.Equal(Sentinel, received.PhoneNumber);
    Assert.Equal(Sentinel, client.CallerNumber);
  }
}
