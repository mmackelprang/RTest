using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Pages;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Components.Pages;

/// <summary>
/// bUnit tests for the System Status card's BELL row (bell-failure handoff §3.6).
///
/// <para>
/// These render the real markup rather than only the rules helper, because the bug
/// being fixed lived in the *template* — <c>Ht801Reachable == true ? "green" : "red"</c>
/// — not in a helper. A rules-only test would have passed against the broken code.
/// </para>
/// </summary>
public class PhoneDashboardPanelTests : TestContext
{
  public PhoneDashboardPanelTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private static PhoneSystemStatusDto Status(bool? reachable) => new()
  {
    Platform = "Linux",
    Ht801IpAddress = "192.168.1.57",
    Ht801Reachable = reachable,
  };

  private IRenderedComponent<PhoneDashboardPanel> Render(PhoneSystemStatusDto? status) =>
    RenderComponent<PhoneDashboardPanel>(p => p
      .Add(x => x.SystemStatus, status)
      .Add(x => x.CallState, new PhoneCallStateDto { CallState = "Idle" }));

  /// <summary>
  /// Returns the pill element from the status row whose label is BELL, so the
  /// assertions cannot accidentally match the Bluetooth / SIP / Platform rows.
  /// </summary>
  private static IElement BellPill(IRenderedComponent<PhoneDashboardPanel> cut)
  {
    var row = cut.FindAll(".phone-status-row")
      .Single(r => r.QuerySelector(".lbl")!.TextContent.Trim() == "Bell");
    return row.QuerySelector(".phone-pill")!;
  }

  // ── The regression test ─────────────────────────────────────────────────────

  [Fact]
  public void BellPill_NullReachable_RendersUnknown_NotOffline()
  {
    // Ht801Reachable is bool?. null means "not yet probed / cannot determine".
    // Before the fix this row rendered `.phone-pill.red` / "Offline" — a false alarm
    // on every cold page load, on the one indicator that would have caught the
    // silent-bell incident. This test fails against that code.
    var pill = BellPill(Render(Status(null)));

    pill.ClassList.Should().Contain("gray");
    pill.TextContent.Trim().Should().Be("Unknown");

    pill.ClassList.Should().NotContain("red");
    pill.TextContent.Should().NotContain("Offline");
  }

  [Fact]
  public void BellPill_NullSystemStatus_RendersUnknown_NotOffline()
  {
    // Same false-alarm shape one level up: the panel renders before any status has
    // been fetched at all.
    var pill = BellPill(Render(null));

    pill.ClassList.Should().Contain("gray");
    pill.TextContent.Trim().Should().Be("Unknown");
    pill.ClassList.Should().NotContain("red");
  }

  // ── The other two states still work ─────────────────────────────────────────

  [Fact]
  public void BellPill_Reachable_RendersOnlineGreen()
  {
    var pill = BellPill(Render(Status(true)));

    pill.ClassList.Should().Contain("green");
    pill.TextContent.Trim().Should().Be("Online");
  }

  [Fact]
  public void BellPill_Unreachable_RendersOfflineRed()
  {
    // A genuine false must still alarm — the fix must not silence real faults.
    var pill = BellPill(Render(Status(false)));

    pill.ClassList.Should().Contain("red");
    pill.TextContent.Trim().Should().Be("Offline");
  }

  [Fact]
  public void BellPill_UnknownAndUnreachable_AreVisuallyDistinct()
  {
    var unknown = BellPill(Render(Status(null)));
    var unreachable = BellPill(Render(Status(false)));

    unknown.ClassName.Should().NotBe(unreachable.ClassName);
    unknown.TextContent.Trim().Should().NotBe(unreachable.TextContent.Trim());
  }

  // ── Relabel (§3.6) ──────────────────────────────────────────────────────────

  [Fact]
  public void BellRow_IsLabelledBell_NotHt801Ata()
  {
    // "Bell" is the only word in this card a non-technical household member reads.
    // (.phone-status-row .lbl is text-transform: uppercase, so this renders as BELL.)
    var cut = Render(Status(true));
    var labels = cut.FindAll(".phone-status-row .lbl").Select(l => l.TextContent.Trim()).ToList();

    labels.Should().Contain("Bell");
    labels.Should().NotContain("HT801 ATA");
  }

  [Fact]
  public void BellRow_KeepsModelNumberAndAddressInValueColumn()
  {
    // The model number is a diagnostic detail, so it moves out of the label column
    // rather than being dropped.
    var cut = Render(Status(true));
    var row = cut.FindAll(".phone-status-row")
      .Single(r => r.QuerySelector(".lbl")!.TextContent.Trim() == "Bell");

    var value = row.QuerySelector(".val")!.TextContent;
    value.Should().Contain("HT801");
    value.Should().Contain("192.168.1.57");
  }
}
