using FluentAssertions;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Models;

/// <summary>
/// Pins the pure bell-health rules (bell-failure handoff §3.6, §5.6, §7m).
///
/// <para>
/// The load-bearing case throughout is <c>Ht801Reachable == null</c>. The shipped code
/// rendered the pill as <c>== true ? green : red</c>, which painted an *unknown* value
/// as a red "Offline" on every cold load — a false alarm on the single indicator that
/// would have caught the silent-bell incident. These tests exist so that collapse
/// cannot come back.
/// </para>
/// </summary>
public class BellHealthRulesTests
{
  private static PhoneSystemStatusDto Status(bool? reachable) =>
    new() { Ht801IpAddress = "192.168.1.57", Ht801Reachable = reachable };

  // ── Derivation ──────────────────────────────────────────────────────────────

  [Fact]
  public void FromSystemStatus_ReachableTrue_IsOk()
  {
    BellHealthRules.FromSystemStatus(Status(true)).Should().Be(BellHealth.Ok);
  }

  [Fact]
  public void FromSystemStatus_ReachableFalse_IsSuspect()
  {
    BellHealthRules.FromSystemStatus(Status(false)).Should().Be(BellHealth.Suspect);
  }

  [Fact]
  public void FromSystemStatus_ReachableNull_IsUnknown_NotSuspect()
  {
    // THE regression case: null means "not probed yet / cannot determine", never false.
    var health = BellHealthRules.FromSystemStatus(Status(null));

    health.Should().Be(BellHealth.Unknown);
    health.Should().NotBe(BellHealth.Suspect);
  }

  [Fact]
  public void FromSystemStatus_NullStatus_IsUnknown()
  {
    // RotaryPhone.API unreachable, or simply not fetched yet. Says nothing about the
    // bell, so it must not alarm.
    BellHealthRules.FromSystemStatus(null).Should().Be(BellHealth.Unknown);
  }

  // ── Fault predicate ─────────────────────────────────────────────────────────

  [Theory]
  [InlineData(BellHealth.Suspect, true)]
  [InlineData(BellHealth.Failed, true)]
  [InlineData(BellHealth.Ok, false)]
  [InlineData(BellHealth.Unknown, false)]   // absence of evidence is not a fault (§7m)
  public void IsFaulted_MatchesSpec(BellHealth health, bool expected)
  {
    BellHealthRules.IsFaulted(health).Should().Be(expected);
  }

  // ── Pill mapping (§3.6) ─────────────────────────────────────────────────────

  [Theory]
  [InlineData(BellHealth.Ok, "green", "Online")]
  [InlineData(BellHealth.Suspect, "red", "Offline")]
  [InlineData(BellHealth.Failed, "red", "Offline")]
  [InlineData(BellHealth.Unknown, "gray", "Unknown")]
  public void Pill_IsTriState(BellHealth health, string expectedClass, string expectedText)
  {
    BellHealthRules.PillClass(health).Should().Be(expectedClass);
    BellHealthRules.PillText(health).Should().Be(expectedText);
  }

  [Fact]
  public void Pill_UnknownIsVisuallyDistinctFromUnreachable()
  {
    // Gray vs red, "Unknown" vs "Offline" — a user must be able to tell "we don't know"
    // apart from "it's broken" without reading the code.
    BellHealthRules.PillClass(BellHealth.Unknown)
      .Should().NotBe(BellHealthRules.PillClass(BellHealth.Suspect));
    BellHealthRules.PillText(BellHealth.Unknown)
      .Should().NotBe(BellHealthRules.PillText(BellHealth.Suspect));
  }

  // ── Nav-pill accessible name (§5.6) ─────────────────────────────────────────

  [Fact]
  public void NavPillAriaLabel_NoFaultNoUnread_IsPlainPhone()
  {
    BellHealthRules.NavPillAriaLabel(BellHealth.Ok, 0).Should().Be("Phone");
  }

  [Fact]
  public void NavPillAriaLabel_UnreadOnly_KeepsExistingWording()
  {
    BellHealthRules.NavPillAriaLabel(BellHealth.Ok, 3).Should().Be("Phone, 3 unread");
  }

  [Fact]
  public void NavPillAriaLabel_FaultOnly_CarriesFaultInText()
  {
    BellHealthRules.NavPillAriaLabel(BellHealth.Suspect, 0)
      .Should().Be("Phone — the phone won't ring");
  }

  [Fact]
  public void NavPillAriaLabel_FaultAndUnread_CarriesBoth()
  {
    BellHealthRules.NavPillAriaLabel(BellHealth.Suspect, 3)
      .Should().Be("Phone, 3 unread — the phone won't ring");
  }

  [Fact]
  public void NavPillAriaLabel_Unknown_DoesNotAnnounceAFault()
  {
    BellHealthRules.NavPillAriaLabel(BellHealth.Unknown, 0).Should().Be("Phone");
    BellHealthRules.NavPillAriaLabel(BellHealth.Unknown, 2).Should().Be("Phone, 2 unread");
  }
}
