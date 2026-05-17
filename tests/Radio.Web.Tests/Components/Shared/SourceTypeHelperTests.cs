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
}
