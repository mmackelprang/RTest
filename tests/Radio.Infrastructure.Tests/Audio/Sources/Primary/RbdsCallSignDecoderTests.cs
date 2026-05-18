using Radio.Infrastructure.Audio.Sources.Primary;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// NRSC-4-B Annex D PI-to-call-sign decode tests. Verifies the algorithm
/// against live-broadcast captured test vectors (WUNC, WSMW), the range
/// boundaries (KAAA, KZZZ, WAAA, WZZZ), and the out-of-range null behaviour
/// for PIs that fall outside the 4-letter K/W North-American range
/// (3-letter call signs, LP stations, international, unencodable).
/// <para>
/// Task #80 v4 — this replaces the abandoned PS-rotation-based stability
/// tracker (deleted in the same commit) with the industry-standard
/// PI-based decode.
/// </para>
/// </summary>
public class RbdsCallSignDecoderTests
{
  // Live-broadcast captured PI codes (Tester's UAT journal):
  //   WUNC = 0x8ACC, WSMW = 0x857E.
  // Range boundaries computed from the base + (25*676 + 25*26 + 25 = 0x44A7):
  //   K-station range: 0x1000 (KAAA) … 0x54A7 (KZZZ)
  //   W-station range: 0x54A8 (WAAA) … 0x994F (WZZZ)
  [Theory]
  [InlineData((ushort)0x8ACC, "WUNC")] // Tester live capture
  [InlineData((ushort)0x857E, "WSMW")] // Tester live capture
  [InlineData((ushort)0x1000, "KAAA")] // K-range minimum
  [InlineData((ushort)0x54A7, "KZZZ")] // K-range maximum
  [InlineData((ushort)0x54A8, "WAAA")] // W-range minimum (one above K-max)
  [InlineData((ushort)0x994F, "WZZZ")] // W-range maximum
  [InlineData((ushort)0x1234, "KAVS")] // synthetic test-seam value
  public void DecodeCallSign_KnownPi_ReturnsExpectedCallSign(ushort pi, string expected)
  {
    Assert.Equal(expected, RbdsCallSignDecoder.DecodeCallSign(pi));
  }

  [Fact]
  public void DecodeCallSign_ZeroPi_ReturnsNull()
  {
    // PI=0x0000 is below the K-range base (0x1000) — caller should fall
    // back to band+frequency display rather than emit a garbage call sign.
    Assert.Null(RbdsCallSignDecoder.DecodeCallSign(0x0000));
  }

  [Fact]
  public void DecodeCallSign_MaxPi_ReturnsNull()
  {
    // PI=0xFFFF is above the W-range max (0x994F) — covers 3-letter call
    // signs, LP stations, international, unencodable assignments. Return
    // null so the caller falls back to band+frequency.
    Assert.Null(RbdsCallSignDecoder.DecodeCallSign(0xFFFF));
  }

  [Fact]
  public void DecodeCallSign_JustBelowKRange_ReturnsNull()
  {
    // 0x0FFF is one below the K-range minimum (0x1000).
    Assert.Null(RbdsCallSignDecoder.DecodeCallSign(0x0FFF));
  }

  [Fact]
  public void DecodeCallSign_JustAboveWRange_ReturnsNull()
  {
    // 0x9950 is one above the W-range maximum (0x994F). This is where
    // 3-letter call sign encodings begin in the NRSC-4-B spec.
    Assert.Null(RbdsCallSignDecoder.DecodeCallSign(0x9950));
  }

  [Fact]
  public void DecodeCallSign_KRangeIsContiguousWithWRange()
  {
    // The K-range maximum and W-range minimum must differ by exactly 1 —
    // there is no PI gap between KZZZ and WAAA. A spec-compliant decode
    // therefore covers every PI in [0x1000, 0x994F] without holes.
    Assert.Equal("KZZZ", RbdsCallSignDecoder.DecodeCallSign(0x54A7));
    Assert.Equal("WAAA", RbdsCallSignDecoder.DecodeCallSign(0x54A8));
  }

  /// <summary>
  /// Round-trips every valid 4-letter combination through encode→decode
  /// for both K-station and W-station prefixes. Any algorithmic error
  /// (boundary mistake, digit-extraction bug, off-by-one) surfaces here
  /// as a mismatched call sign, immediately.
  /// </summary>
  [Fact]
  public void DecodeCallSign_RoundTrip_AllValidLetters()
  {
    const ushort kBase = 0x1000;
    const ushort wBase = 0x54A8;

    for (int a = 0; a < 26; a++)
    {
      for (int b = 0; b < 26; b++)
      {
        for (int c = 0; c < 26; c++)
        {
          // K-station round trip — PI = 0x1000 + (a*676 + b*26 + c).
          var kPi = (ushort)(kBase + (a * 676) + (b * 26) + c);
          var kExpected = string.Concat("K", (char)('A' + a), (char)('A' + b), (char)('A' + c));
          Assert.Equal(kExpected, RbdsCallSignDecoder.DecodeCallSign(kPi));

          // W-station round trip — PI = 0x54A8 + (a*676 + b*26 + c).
          var wPi = (ushort)(wBase + (a * 676) + (b * 26) + c);
          var wExpected = string.Concat("W", (char)('A' + a), (char)('A' + b), (char)('A' + c));
          Assert.Equal(wExpected, RbdsCallSignDecoder.DecodeCallSign(wPi));
        }
      }
    }
  }

  /// <summary>
  /// Sanity-check the live-capture test vectors against the raw formula.
  /// This is essentially the math that backed the inline-data verification
  /// — kept as a standalone test so a future algorithm regression surfaces
  /// with a clear "the boundary is wrong" message rather than the round-trip
  /// loop's catch-all failure.
  /// </summary>
  [Fact]
  public void DecodeCallSign_LiveCaptureMathHolds()
  {
    // WUNC: PI = 0x8ACC, 0x8ACC - 0x54A8 = 0x3624 = 13860.
    // 13860 = 20*676 + 13*26 + 2 → W + U + N + C = "WUNC".
    Assert.Equal(13860, 0x8ACC - 0x54A8);
    Assert.Equal(20, 13860 / 676); // U
    Assert.Equal(13, (13860 / 26) % 26); // N
    Assert.Equal(2, 13860 % 26); // C
    Assert.Equal("WUNC", RbdsCallSignDecoder.DecodeCallSign(0x8ACC));

    // WSMW: PI = 0x857E, 0x857E - 0x54A8 = 0x30D6 = 12502.
    // 12502 = 18*676 + 12*26 + 22 → W + S + M + W = "WSMW".
    Assert.Equal(12502, 0x857E - 0x54A8);
    Assert.Equal(18, 12502 / 676); // S
    Assert.Equal(12, (12502 / 26) % 26); // M
    Assert.Equal(22, 12502 % 26); // W
    Assert.Equal("WSMW", RbdsCallSignDecoder.DecodeCallSign(0x857E));
  }
}
