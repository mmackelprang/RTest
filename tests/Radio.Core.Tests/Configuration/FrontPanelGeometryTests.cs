using Radio.Core.Configuration;

namespace Radio.Core.Tests.Configuration;

/// <summary>
/// Pins the one definition of the front panel against the drawing it derives from,
/// <c>design/hardware/front-panel-layout_4.svg</c>.
///
/// <para>
/// These are deliberately not tautologies over the constants. Each one re-derives a value from an
/// independent fact in the drawing — the panel and LCD rectangles, the uniform pitch, the VESA-75
/// reference square — so a number edited here without the drawing changing fails. The point of the
/// class under test is that a recut moves one definition; the point of these tests is that the
/// definition still agrees with the recut drawing when it does.
/// </para>
/// </summary>
public class FrontPanelGeometryTests
{
  // Independent facts from the drawing (handoff §9.4), not read back from the class under test.
  private const double PanelHeightMm = 152.4;
  private const double LcdActiveHeightMm = 119.89;
  private const double PitchMm = 29.63;
  private const int ViewportPx = 720;

  [Fact]
  public void FourKnobs_InTheEngravedOrder()
  {
    Assert.Equal(FrontPanelGeometry.EncoderCount, FrontPanelGeometry.Encoders.Count);
    Assert.Equal(
      new[] { "VOLUME", "SOURCE", "PRESETS", "TUNING" },
      FrontPanelGeometry.Encoders.Select(e => e.EngravedName));
    Assert.Equal(new[] { 0, 1, 2, 3 }, FrontPanelGeometry.Encoders.Select(e => e.Index));
  }

  [Fact]
  public void PanelPositions_AreOnAUniformPitch()
  {
    var centres = FrontPanelGeometry.Encoders.Select(e => e.PanelCentreYMm).ToList();

    for (var i = 1; i < centres.Count; i++)
    {
      // The drawing states its centres to 2 dp, so consecutive gaps differ by up to
      // 0.01 mm; the tolerance absorbs that rather than pretending the pitch is exact.
      Assert.Equal(PitchMm, centres[i] - centres[i - 1], 0.011);
    }
  }

  [Fact]
  public void TheColumn_IsCentredOnThePanel()
  {
    var centres = FrontPanelGeometry.Encoders.Select(e => e.PanelCentreYMm).ToList();

    Assert.Equal(PanelHeightMm / 2, (centres[0] + centres[^1]) / 2, 0.011);
    // Column span, as drawn.
    Assert.Equal(88.90, centres[^1] - centres[0], 0.011);
  }

  [Theory]
  [InlineData(0, 93.05)]
  [InlineData(1, 271.02)]
  [InlineData(2, 448.98)]
  [InlineData(3, 626.95)]
  public void EachBand_IsTheMeasuredProjectionRoundedToACleanQuarter(int index, double measuredPx)
  {
    // The LCD's active area is vertically centred on the panel, so a knob's panel height projects
    // onto the viewport at (panelY - lcdTop) / lcdHeight * 720.
    var lcdTopMm = (PanelHeightMm - LcdActiveHeightMm) / 2;
    var knob = FrontPanelGeometry.Encoders[index];

    var projected = (knob.PanelCentreYMm - lcdTopMm) / LcdActiveHeightMm * ViewportPx;

    // 0.05 px, because the drawing states its knob centres to 2 dp: re-deriving the projection
    // from those rounded millimetres cannot reproduce the handoff table exactly, and 0.05 px is
    // four orders of magnitude below the 3.05 px the rounding to clean bands already spends.
    Assert.Equal(measuredPx, projected, 0.05);

    // The shipped band is that projection rounded to the clean quarters of the 720 px axis, and
    // the bound IS the argument: the handoff accepts a maximum deviation of 3.05 px (0.508 mm on
    // the panel) because the nearest wrong band is 178 px away. 3.1 rather than 3.05 because the
    // handoff quotes its own figure to 2 dp - the deviation re-derives to 3.055 - so this tests
    // the claim rather than the rounding of it.
    Assert.True(
      Math.Abs(knob.HudBandYPx - projected) <= 3.1,
      $"band {knob.HudBandYPx} is more than 3.1 px from the measured {projected:F2}");
    Assert.Equal(0, knob.HudBandYPx % 90);
  }

  [Fact]
  public void Bands_AreTheCleanQuartersAndFitInsideTheViewport()
  {
    var bands = FrontPanelGeometry.Encoders.Select(e => e.HudBandYPx).ToList();

    Assert.Equal(new[] { 90, 270, 450, 630 }, bands);
    Assert.All(bands, b => Assert.InRange(b, 1, FrontPanelGeometry.ViewportHeightPx - 1));
  }

  [Theory]
  [InlineData(-1, 0)]
  [InlineData(-99, 0)]
  [InlineData(4, 3)]
  [InlineData(99, 3)]
  public void ForIndex_ClampsRatherThanThrowing(int requested, int expectedIndex)
  {
    // The index arrives over the wire. A host reporting a fifth encoder should land a card
    // somewhere on screen, not take the render down.
    Assert.Equal(expectedIndex, FrontPanelGeometry.ForIndex(requested).Index);
  }

  [Fact]
  public void ForIndex_ReturnsTheKnobAtThatIndex()
  {
    for (var i = 0; i < FrontPanelGeometry.EncoderCount; i++)
    {
      Assert.Same(FrontPanelGeometry.Encoders[i], FrontPanelGeometry.ForIndex(i));
    }
  }

  [Fact]
  public void TheDrawingScale_MatchesTheVesaReferenceSquare()
  {
    // 742.5992 - 530.0008 = 212.5984 user units across a 75.000 mm VESA-75 square.
    const double vesaSpanPx = 742.5992 - 530.0008;

    // DrawingPxPerMm is the scale to 4 dp (exactly, it is 212.5984 / 75 = 2.8346453...), so the
    // round trip lands ~0.0012 mm out. That is the constant rounding, not the drawing drifting.
    Assert.Equal(75.0, vesaSpanPx / FrontPanelGeometry.DrawingPxPerMm, 0.002);
  }
}
