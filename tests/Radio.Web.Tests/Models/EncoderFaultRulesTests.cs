using System.Linq;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Models;

/// <summary>
/// Pins the pure encoder fault rules (ENC-12; encoder handoff §7.3, §7.4, §7.6).
///
/// <para>
/// This is where essentially all of the badge's test value lives. <c>MainLayout</c> cannot be
/// rendered in bUnit — <c>MainLayoutTests</c> is a documented stub that renders nothing — so a badge
/// implemented as inline <c>@if</c> logic in that file would ship with no automated coverage at all.
/// The rules were extracted so they could be tested here; the markup that consumes them is covered
/// by the browser Test Plan and by nothing else.
/// </para>
/// </summary>
public class EncoderFaultRulesTests
{
  [Theory]
  [InlineData("Configured")]
  [InlineData("Transient")]
  [InlineData("Unknown")]
  [InlineData("SomeTierFromANewerBuild")]
  [InlineData(null)]
  public void HealthyOrUnrecognisedTiers_ShowNoBadge(string? status)
  {
    // Transient is silent BY DESIGN (handoff §7.6): a USB peripheral missing a report on the first
    // try is ordinary, and badging it would train the owner to ignore the badge that matters. An
    // unrecognised tier is silent so a newer API build degrades to nothing rather than to noise.
    Assert.Equal(EncoderFaultLevel.None, EncoderFaultRules.Level(status, isConnected: true));
    Assert.Equal("", EncoderFaultRules.BadgeIcon(status, isConnected: true));
    Assert.Equal("", EncoderFaultRules.BadgeClass(status, isConnected: true));
    Assert.Equal("Settings", EncoderFaultRules.NavPillAriaLabel(status, isConnected: true));
  }

  [Fact]
  public void DisabledEncoders_ShowNothingAtAll()
  {
    // The owner switched the knobs off deliberately and must not be nagged about the consequence.
    Assert.Equal(EncoderFaultLevel.None,
      EncoderFaultRules.Level("HardFault", isConnected: false, encoderEnabled: false));
    Assert.Equal("", EncoderFaultRules.BadgeIcon("HardFault", isConnected: false, encoderEnabled: false));
    Assert.Equal("Settings",
      EncoderFaultRules.NavPillAriaLabel("HardFault", isConnected: false, encoderEnabled: false));
  }

  [Fact]
  public void EachReportableStateHasItsOwnGlyph_NotJustItsOwnColour()
  {
    // WCAG 1.4.1 and bell handoff §8.3. Colour alone is not a signal.
    var icons = new[]
    {
      EncoderFaultRules.BadgeIcon("Degraded", isConnected: true),
      EncoderFaultRules.BadgeIcon("HardFault", isConnected: true),
      EncoderFaultRules.BadgeIcon("Configured", isConnected: false),
    };
    Assert.Equal(icons.Length, icons.Distinct().Count());
    Assert.All(icons, i => Assert.NotEqual("", i));
  }

  [Fact]
  public void AriaLabel_CarriesTheStateInWords_ForEveryReportableState()
  {
    Assert.Contains("volume limited", EncoderFaultRules.NavPillAriaLabel("HardFault", true));
    Assert.Contains("not applied", EncoderFaultRules.NavPillAriaLabel("Degraded", true));
    Assert.Contains("not connected", EncoderFaultRules.NavPillAriaLabel("Configured", false));
  }

  [Fact]
  public void AbsenceOutranksAStaleConfigurationTier()
  {
    Assert.Equal("link_off", EncoderFaultRules.BadgeIcon("HardFault", isConnected: false));
  }
}
