using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.External;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

/// <summary>
/// PHN-12: what <see cref="PhoneCallClient"/> raises for each of RotaryPhone's hub events, driven through
/// the internal handlers the live registrations are wired to. The ARGUMENT ORDER itself is pinned over a
/// real SignalR connection by <c>PhoneCallClientContractTests</c> in Radio.API.Tests; this file pins the
/// event semantics on top of it.
/// </summary>
public class PhoneCallClientHubEventTests
{
  private const string Number = "5550137424";

  private static (PhoneCallClient Client, List<PhoneCallStateChangedEventArgs> Raised) Build()
  {
    var options = new Mock<IOptionsMonitor<PhoneIntegrationOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new PhoneIntegrationOptions());
    var client = new PhoneCallClient(NullLogger<PhoneCallClient>.Instance, options.Object);
    var raised = new List<PhoneCallStateChangedEventArgs>();
    client.CallStateChanged += (_, e) => raised.Add(e);
    return (client, raised);
  }

  [Fact]
  public void ARing_IsRaisedOnce_FromTheIncomingCall_WithTheNumber()
  {
    var (client, raised) = Build();

    client.OnHubCallStateChanged("default", "Ringing");
    var afterStateOnly = raised.Count;
    client.OnHubIncomingCall("default", Number);

    Assert.Equal(0, afterStateOnly);
    var ring = Assert.Single(raised);
    Assert.Equal(PhoneCallState.Ringing, ring.State);
    Assert.Equal(Number, ring.PhoneNumber);
    Assert.Equal(PhoneCallState.Ringing, client.CurrentState);
  }

  [Fact]
  public void ADuplicateIncomingCall_WithNoRingingBetween_IsNotRaisedAgain()
  {
    var (client, raised) = Build();

    client.OnHubCallStateChanged("default", "Ringing");
    client.OnHubIncomingCall("default", Number);
    client.OnHubIncomingCall("default", Number);

    Assert.Single(raised);
  }

  [Fact]
  public void AfterAMissedIdle_TheSameCallersNextCall_IsStillRaised()
  {
    // A hub drop swallowed the Idle that ended the first call, so the client still thinks it is ringing.
    // RotaryPhone sends Ringing before every IncomingCall, and that is what lets the second call through.
    var (client, raised) = Build();
    client.OnHubCallStateChanged("default", "Ringing");
    client.OnHubIncomingCall("default", Number);

    client.OnHubCallStateChanged("default", "Ringing");
    client.OnHubIncomingCall("default", Number);

    Assert.Equal(2, raised.Count(e => e.State == PhoneCallState.Ringing));
  }

  [Fact]
  public void NoCallerId_IsRaisedAsNoNumber_AndTheRealNumberAfterIt_IsRaisedAgain()
  {
    // RotaryPhone's CallManager re-broadcasts when +CLIP replaces "Unknown" mid-ring. "Unknown" itself must
    // not reach the announcement as if it were a number ("Incoming call from Unknown").
    var (client, raised) = Build();

    client.OnHubCallStateChanged("default", "Ringing");
    client.OnHubIncomingCall("default", "Unknown");
    var withheld = client.CallerNumber;
    client.OnHubCallStateChanged("default", "Ringing");
    client.OnHubIncomingCall("default", Number);

    Assert.Equal(new string?[] { null, Number }, raised.Select(e => e.PhoneNumber));
    Assert.Null(withheld);
  }

  [Theory]
  [InlineData("InCall", PhoneCallState.InCall)]
  [InlineData("Idle", PhoneCallState.Ended)]
  public void LeavingTheRing_IsRaised_AndClearsTheCaller(string state, PhoneCallState expected)
  {
    var (client, raised) = Build();
    client.OnHubIncomingCall("default", Number);

    client.OnHubCallStateChanged("default", state);

    Assert.Equal(expected, raised[^1].State);
    Assert.Null(raised[^1].PhoneNumber);
    Assert.Null(client.CallerNumber);
  }

  [Fact]
  public void TheSameCallerCallingBack_AfterTheFirstCallEnded_IsANewRing()
  {
    var (client, raised) = Build();
    client.OnHubCallStateChanged("default", "Ringing");
    client.OnHubIncomingCall("default", Number);
    client.OnHubCallStateChanged("default", "Idle");

    client.OnHubCallStateChanged("default", "Ringing");
    client.OnHubIncomingCall("default", Number);

    Assert.Equal(2, raised.Count(e => e.State == PhoneCallState.Ringing));
  }

  [Fact]
  public void ThePhoneIdIsNeverReadAsTheState()
  {
    // The defect PHN-12 fixed: (phoneId, state) bound as (state, number) made every event "Idle" (the
    // phone id matches no state). A phone id that happens to spell a state must still be a phone id.
    var (client, raised) = Build();

    client.OnHubCallStateChanged("ringing", "Idle");

    Assert.Equal(PhoneCallState.Ended, Assert.Single(raised).State);
  }
}
