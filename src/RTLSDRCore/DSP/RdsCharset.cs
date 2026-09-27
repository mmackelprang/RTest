namespace RTLSDRCore.DSP;

/// <summary>
/// The RDS basic character code table (IEC 62106 / NRSC-4-B, code table E.1)
/// for the Program Service name and RadioText fields.
/// </summary>
/// <remarks>
/// <para>
/// RDS text is not ISO-8859-1: 0x80–0xFF carry accented letters and symbols
/// from the standard's own table. Before this class the decoder validated
/// text bytes against printable ASCII (0x20–0x7E) and threw away anything
/// else — which dropped a whole PS segment when one byte was accented, so the
/// slot kept the previous page's characters for as long as the station
/// rolled (<c>AUD-69</c>), and left a RadioText containing one accented
/// character permanently incomplete (<c>AUD-70</c>).
/// </para>
/// <para>
/// 0x20–0x7E map to ASCII unchanged. The standard's table differs from ASCII
/// at four positions (0x24 ¤, 0x5E ―, 0x60 ‖, 0x7E ¯), but broadcasters in
/// practice send ASCII there and consumer receivers display ASCII, so this
/// decoder keeps the ASCII reading. Control codes (0x00–0x1F, 0x7F) are not
/// characters; <see cref="TryDecode"/> reports them as undecodable and the
/// callers decide what a control code means for their field (0x0D terminates
/// a RadioText message, 0x0A/0x0B break a line).
/// </para>
/// </remarks>
internal static class RdsCharset
{
  // Rows 0x80–0xFF of code table E.1, sixteen entries per row. 0xFF has no
  // assignment in the table and is rendered as a space.
  private static readonly string HighHalf =
    "áàéèíìóòúùÑÇŞβ¡Ĳ" +   // 0x80
    "âäêëîïôöûüñçşǧıĳ" +   // 0x90
    "ªα©‰Ǧěňőπ€£$←↑→↓" +   // 0xA0
    "º¹²³±İńűµ¿÷°¼½¾§" +   // 0xB0
    "ÁÀÉÈÍÌÓÒÚÙŘČŠŽÐĿ" +   // 0xC0
    "ÂÄÊËÎÏÔÖÛÜřčšžđŀ" +   // 0xD0
    "ÃÅÆŒŷÝÕØÞŊŔĆŚŹŦð" +   // 0xE0
    "ãåæœŵýõøþŋŕćśźŧ ";    // 0xF0

  /// <summary>
  /// Decodes one RDS text byte.
  /// </summary>
  /// <param name="code">The byte as transmitted in block C or D.</param>
  /// <param name="c">The character it denotes when the method returns true.</param>
  /// <returns>
  /// True for a displayable character (0x20–0x7E as ASCII, 0x80–0xFF via the
  /// basic code table); false for a control code (0x00–0x1F, 0x7F).
  /// </returns>
  public static bool TryDecode(byte code, out char c)
  {
    if (code >= 0x20 && code <= 0x7E)
    {
      c = (char)code;
      return true;
    }

    if (code >= 0x80)
    {
      c = HighHalf[code - 0x80];
      return true;
    }

    c = '\0';
    return false;
  }
}
