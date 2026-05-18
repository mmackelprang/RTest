namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Decodes North-American FCC call signs from RDS PI (Program
/// Identification) codes per NRSC-4-B Annex D (Radio Broadcast Data
/// System / RBDS). The PI code is a 16-bit station identifier broadcast
/// on Block A of every RDS group and — unlike the rotating Program
/// Service (PS) name — does <b>not</b> change with songs / DJ names /
/// phone numbers. This makes PI the correct source for the station's
/// "stable" identifier.
/// <para>
/// <b>Algorithm (4-letter call signs):</b>
/// Stations whose call signs begin with K or W use a deterministic
/// base-26 encoding of the last three letters.
/// </para>
/// <list type="bullet">
///   <item>K-stations: PI ∈ [0x1000, 0x54A7], <c>PI = 0x1000 + (P1·676 + P2·26 + P3)</c></item>
///   <item>W-stations: PI ∈ [0x54A8, 0x994F], <c>PI = 0x54A8 + (P1·676 + P2·26 + P3)</c></item>
/// </list>
/// <para>
/// where P1, P2, P3 are zero-based letter indices (A=0, B=1, …, Z=25)
/// for the 2nd, 3rd, and 4th letters of the call sign. Decoding is the
/// inverse: subtract the range base, then extract the three base-26
/// digits.
/// </para>
/// <para>
/// <b>Out-of-range PIs return null.</b> The 4-letter K/W range covers
/// the bulk of US/Canadian FM broadcasters; outside that range we have:
/// 3-letter call signs (rare; older AM stations; different sub-range),
/// LP / unencodable PI assignments, and international stations. These
/// callers should fall back to band + frequency display.
/// </para>
/// <para>
/// <b>Verified test vectors:</b>
/// </para>
/// <list type="bullet">
///   <item>WUNC = 0x8ACC: 0x8ACC − 0x54A8 = 13860 = 20·676 + 13·26 + 2 → WUNC</item>
///   <item>WSMW = 0x857E: 0x857E − 0x54A8 = 12502 = 18·676 + 12·26 + 22 → WSMW</item>
///   <item>KAAA = 0x1000: smallest K-station PI</item>
///   <item>KZZZ = 0x54A7: largest K-station PI (25·676 + 25·26 + 25 = 0x44A7; +0x1000)</item>
///   <item>WAAA = 0x54A8: smallest W-station PI</item>
///   <item>WZZZ = 0x994F: largest W-station PI (25·676 + 25·26 + 25 = 0x44A7; +0x54A8)</item>
/// </list>
/// </summary>
public static class RbdsCallSignDecoder
{
  // Base values for the K-prefix and W-prefix ranges per NRSC-4-B Annex D.
  private const ushort KStationBase = 0x1000;
  private const ushort KStationMax = 0x54A7;
  private const ushort WStationBase = 0x54A8;
  // 0x54A8 + 25*676 + 25*26 + 25 = 0x994F is the highest valid 4-letter
  // W-station PI; anything higher is either a 3-letter call sign, LP
  // station, or unencodable assignment and we return null.
  private const ushort WStationMax = 0x994F;

  /// <summary>
  /// Decodes a 4-letter FCC call sign from an RBDS PI code, or returns
  /// null if the PI falls outside the 4-letter K/W range covered by
  /// NRSC-4-B Annex D.
  /// </summary>
  /// <param name="pi">The 16-bit PI code from RDS Block A.</param>
  /// <returns>The decoded 4-character call sign (e.g. "WUNC"), or null.</returns>
  public static string? DecodeCallSign(ushort pi)
  {
    // K-station range: first letter is "K".
    if (pi >= KStationBase && pi <= KStationMax)
    {
      return DecodeFromBase(pi, KStationBase, 'K');
    }

    // W-station range: first letter is "W".
    if (pi >= WStationBase && pi <= WStationMax)
    {
      return DecodeFromBase(pi, WStationBase, 'W');
    }

    // Outside the 4-letter K/W range — 3-letter call signs, LP stations,
    // international assignments, or noise. Caller should fall back to
    // band + frequency display rather than guessing.
    return null;
  }

  private static string DecodeFromBase(ushort pi, ushort rangeBase, char firstLetter)
  {
    int n = pi - rangeBase;
    int p1 = n / 676;
    int p2 = (n / 26) % 26;
    int p3 = n % 26;

    // Defense-in-depth: by construction (range guards above) p1/p2/p3 are
    // in [0, 25], but keep the check so a future range-bound mistake
    // surfaces as null instead of a garbage character.
    if (p1 < 0 || p1 > 25 || p2 < 0 || p2 > 25 || p3 < 0 || p3 > 25)
    {
      return string.Empty;
    }

    Span<char> buf = stackalloc char[4];
    buf[0] = firstLetter;
    buf[1] = (char)('A' + p1);
    buf[2] = (char)('A' + p2);
    buf[3] = (char)('A' + p3);
    return new string(buf);
  }
}
