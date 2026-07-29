using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radio.Web.Components.Shared;
using Radio.Web.Models;
using Xunit;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the hero's degraded idle state — State C in the bell-failure
/// handoff (§3.3). When the ATA is known-unreachable the existing empty-state copy
/// ("wait for an incoming ring") is an active lie, so it is replaced by an honest
/// strip that redirects the user to the screen.
/// </summary>
public class PhoneStatusHeroTests : TestContext
{
  public PhoneStatusHeroTests()
  {
    Services.AddRadzenComponents();
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private IRenderedComponent<PhoneStatusHero> Render(
    BellHealth health, string callState = "Idle", string? incomingNumber = null) =>
    RenderComponent<PhoneStatusHero>(p => p
      .Add(x => x.BellHealth, health)
      .Add(x => x.CallState, new PhoneCallStateDto
      {
        CallState = callState,
        IncomingNumber = incomingNumber,
      }));

  // ── State C appears only for a known-unreachable ATA ────────────────────────

  [Fact]
  public void Idle_Suspect_ShowsDegradedStrip_InsteadOfWaitForRingCopy()
  {
    var cut = Render(BellHealth.Suspect);

    var strip = cut.Find(".phone-hero-alert");
    strip.TextContent.Should().Contain("The phone can't ring right now");
    // The second sentence is essential: without it the reasonable inference is
    // "the phone is dead", and someone stops watching for calls entirely.
    strip.TextContent.Should().Contain("Calls will still appear on this screen");

    cut.FindAll(".phone-hero-empty").Should().BeEmpty();
    cut.Markup.Should().NotContain("wait for an incoming ring");
  }

  [Fact]
  public void Idle_Unknown_ShowsNormalEmptyState_NoAlarm()
  {
    // §7m — never alarm on absence of evidence. This is the cold-load case.
    var cut = Render(BellHealth.Unknown);

    cut.FindAll(".phone-hero-alert").Should().BeEmpty();
    cut.Find(".phone-hero-empty").TextContent.Should().Contain("wait for an incoming ring");
  }

  [Fact]
  public void Idle_Ok_ShowsNormalEmptyState()
  {
    var cut = Render(BellHealth.Ok);

    cut.FindAll(".phone-hero-alert").Should().BeEmpty();
    cut.Find(".phone-hero-empty").TextContent.Should().Contain("wait for an incoming ring");
  }

  [Fact]
  public void DefaultParameter_IsUnknown_SoAnUnwiredCallerCannotFalseAlarm()
  {
    var cut = RenderComponent<PhoneStatusHero>(p => p
      .Add(x => x.CallState, new PhoneCallStateDto { CallState = "Idle" }));

    cut.FindAll(".phone-hero-alert").Should().BeEmpty();
  }

  // ── State C is idle-only; the live-call treatment is a separate PR ──────────

  [Theory]
  [InlineData("Ringing")]
  [InlineData("InCall")]
  [InlineData("Dialing")]
  public void NonIdleStates_DoNotShowTheIdleStrip(string callState)
  {
    // The during-ring strip has different copy, different severity and an assertive
    // announcement; it ships with the backend BellInviteFailed event (§3.2, §8.1).
    // The idle strip must not leak into those states even when the number is unknown
    // and the hero would otherwise fall through to its empty branch.
    var cut = Render(BellHealth.Suspect, callState);

    cut.FindAll(".phone-hero-alert").Should().BeEmpty();
  }

  [Fact]
  public void ActiveCallWithNumber_RendersMetaRow_NotTheStrip()
  {
    var cut = Render(BellHealth.Suspect, "Ringing", "+18015550134");

    cut.FindAll(".phone-hero-alert").Should().BeEmpty();
    cut.Find(".phone-hero-meta").TextContent.Should().Contain("+18015550134");
  }

  // ── Accessibility (§8.1, §8.3, §5.6) ────────────────────────────────────────

  [Fact]
  public void Strip_AnnouncesPolitely_NotAssertively()
  {
    // An ambient condition with no deadline. role="alert" is reserved for the live
    // during-ring strip (§8.1).
    var strip = Render(BellHealth.Suspect).Find(".phone-hero-alert");

    strip.GetAttribute("role").Should().Be("status");
  }

  [Fact]
  public void Strip_CarriesACrossedBellGlyph_HiddenFromScreenReaders()
  {
    // The glyph is a mandatory non-colour channel (§8.3) — it must survive for a user
    // who cannot distinguish red — but the sentence beside it already says the same
    // thing, so the icon itself is decorative to assistive tech.
    var cut = Render(BellHealth.Suspect);
    var icon = cut.Find(".phone-hero-alert .rzi");

    // Radzen renders Material Symbols as a ligature in the element's text content.
    icon.TextContent.Trim().Should().Be("notifications_off");
    icon.GetAttribute("aria-hidden").Should().Be("true");
  }

  [Fact]
  public void Strip_UsesTheHouseholdWord_NotTheModelNumber()
  {
    // The hero says "rotary phone"; the model number lives one card over in System
    // Status where technical rows belong (§3.3).
    var strip = Render(BellHealth.Suspect).Find(".phone-hero-alert");

    strip.QuerySelector(".phone-hero-alert-sub")!.TextContent.Trim()
      .Should().Be("Rotary phone unreachable");
    strip.TextContent.Should().NotContain("HT801");
  }

  [Fact]
  public void CheckAgainButton_HasAnAccessibleName_AndFiresTheCallback()
  {
    var fired = 0;
    var cut = RenderComponent<PhoneStatusHero>(p => p
      .Add(x => x.BellHealth, BellHealth.Suspect)
      .Add(x => x.CallState, new PhoneCallStateDto { CallState = "Idle" })
      .Add(x => x.OnCheckBell, EventCallback.Factory.Create(this, () => fired++)));

    var button = cut.Find(".phone-hero-alert .phone-btn-sm");
    button.GetAttribute("aria-label").Should().Be("Check whether the phone can ring");

    button.Click();
    fired.Should().Be(1);
  }
}
