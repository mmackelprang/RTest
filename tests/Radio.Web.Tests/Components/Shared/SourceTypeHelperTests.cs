using FluentAssertions;
using Radio.Web.Components.Shared;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// Unit tests for <see cref="SourceTypeHelper"/>. The helper is the single
/// source of truth for the per-source-type matrix that drives the topbar
/// (icon + data attribute + accent var + chevron-affordance gating).
///
/// PR 5 of the design tightening arc (handoff §P1·2) added <c>HasDetail</c>
/// + <c>GetAccentVar</c> to this helper so the chevron / detail-surface
/// dispatch and the source-color dot (NowPlayingDock, IN cluster) share
/// one matrix. These tests pin the matrix so adding a new source type
/// elsewhere can't accidentally drift the chevron behaviour.
/// </summary>
public class SourceTypeHelperTests
{
  [Theory]
  [InlineData("Radio")]
  [InlineData("RTLSDRCore")]
  [InlineData("RF320")]
  [InlineData("Bluetooth")]
  public void HasDetail_ReturnsTrue_ForRadioFamilyAndBluetooth(string sourceType)
  {
    SourceTypeHelper.HasDetail(sourceType).Should().BeTrue();
  }

  [Theory]
  [InlineData("File")]
  [InlineData("FilePlayer")]
  [InlineData("Vinyl")]
  [InlineData("GenericUSB")]
  [InlineData("USB")]
  [InlineData("TTS")]
  [InlineData("TestTone")]
  [InlineData("Spotify")]
  [InlineData("")]
  public void HasDetail_ReturnsFalse_ForSourcesWithoutDedicatedSurface(string sourceType)
  {
    SourceTypeHelper.HasDetail(sourceType).Should().BeFalse();
  }

  [Fact]
  public void HasDetail_IsCaseInsensitive()
  {
    // The hash set uses OrdinalIgnoreCase so the API surface is robust to
    // capitalization drift between consumers (e.g. JSON deserialization
    // can occasionally re-case enum strings).
    SourceTypeHelper.HasDetail("RADIO").Should().BeTrue();
    SourceTypeHelper.HasDetail("radio").Should().BeTrue();
    SourceTypeHelper.HasDetail("bluetooth").Should().BeTrue();
  }

  [Theory]
  [InlineData("Radio", "--source-radio")]
  [InlineData("RTLSDRCore", "--source-radio")]
  [InlineData("RF320", "--source-radio")]
  [InlineData("Bluetooth", "--source-bluetooth")]
  [InlineData("FilePlayer", "--source-file")]
  [InlineData("File", "--source-file")]
  [InlineData("Vinyl", "--source-vinyl")]
  [InlineData("GenericUSB", "--source-usb")]
  [InlineData("USB", "--source-usb")]
  [InlineData("", "--accent-primary")]
  [InlineData("UnknownFutureSource", "--accent-primary")]
  public void GetAccentVar_MapsKnownSourceTypes_AndFallsBackToAccentPrimary(string sourceType, string expected)
  {
    SourceTypeHelper.GetAccentVar(sourceType).Should().Be(expected);
  }

  // ─── IsRadioFamily ────────────────────────────────────────────────────────
  // IsRadioFamily is the SSOT for the radio control panel + tuning workflow.
  // It is deliberately a narrower set than HasDetail — Bluetooth has a detail
  // surface (pairing page) but is NOT a radio source and must return false here.
  // Two call sites delegate to this method: MainLayout.IsRadioSource (chevron
  // click dispatch) and QueueHistoryPanel._isRadioActive (default-tab gating).

  [Fact]
  public void IsRadioFamily_Radio_True()
  {
    SourceTypeHelper.IsRadioFamily("Radio").Should().BeTrue();
  }

  [Fact]
  public void IsRadioFamily_RTLSDRCore_True()
  {
    SourceTypeHelper.IsRadioFamily("RTLSDRCore").Should().BeTrue();
  }

  [Fact]
  public void IsRadioFamily_RF320_True()
  {
    SourceTypeHelper.IsRadioFamily("RF320").Should().BeTrue();
  }

  [Fact]
  public void IsRadioFamily_Bluetooth_False()
  {
    // Important boundary: Bluetooth has detail (HasDetail==true) but is NOT a
    // radio family member. The two matrices are related but distinct.
    SourceTypeHelper.IsRadioFamily("Bluetooth").Should().BeFalse();
  }

  [Theory]
  [InlineData("File")]
  [InlineData("FilePlayer")]
  [InlineData("USB")]
  [InlineData("GenericUSB")]
  [InlineData("Vinyl")]
  [InlineData("TestTone")]
  [InlineData("Spotify")]
  [InlineData("UnknownFutureSource")]
  public void IsRadioFamily_NonRadioSources_False(string sourceType)
  {
    SourceTypeHelper.IsRadioFamily(sourceType).Should().BeFalse();
  }

  [Fact]
  public void IsRadioFamily_CaseInsensitive_True()
  {
    // Matches the OrdinalIgnoreCase contract used by HasDetail — capitalization
    // drift between JSON deserialization and constant strings must not break
    // the chevron-click → radio-panel dispatch.
    SourceTypeHelper.IsRadioFamily("radio").Should().BeTrue();
    SourceTypeHelper.IsRadioFamily("RADIO").Should().BeTrue();
    SourceTypeHelper.IsRadioFamily("rtlsdrcore").Should().BeTrue();
    SourceTypeHelper.IsRadioFamily("rf320").Should().BeTrue();
  }

  [Fact]
  public void IsRadioFamily_Null_False()
  {
    SourceTypeHelper.IsRadioFamily(null).Should().BeFalse();
  }

  [Fact]
  public void IsRadioFamily_Empty_False()
  {
    SourceTypeHelper.IsRadioFamily(string.Empty).Should().BeFalse();
  }
}
