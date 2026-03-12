using System.Text;
using Radio.Core.Models;
using Radio.Core.Utilities;

namespace Radio.Infrastructure.Bluetooth;

public static class VCardParser
{
  public static List<PbapContact> Parse(string vcfContent)
  {
    if (string.IsNullOrWhiteSpace(vcfContent))
      return new List<PbapContact>();

    var contacts = new List<PbapContact>();

    // Unfold continuation lines (RFC 2426: lines starting with space/tab are continuations)
    var unfolded = vcfContent
      .Replace("\r\n ", "")
      .Replace("\r\n\t", "")
      .Replace("\n ", "")
      .Replace("\n\t", "");

    var lines = unfolded.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    string? currentName = null;
    var currentNumbers = new List<string>();
    var inCard = false;

    foreach (var line in lines)
    {
      var trimmed = line.Trim();

      if (trimmed.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
      {
        inCard = true;
        currentName = null;
        currentNumbers = new List<string>();
        continue;
      }

      if (trimmed.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
      {
        if (inCard && currentName != null && currentNumbers.Count > 0)
        {
          contacts.Add(new PbapContact
          {
            DisplayName = currentName,
            PhoneNumbers = currentNumbers
          });
        }
        inCard = false;
        continue;
      }

      if (!inCard) continue;

      if (trimmed.StartsWith("FN:", StringComparison.OrdinalIgnoreCase)
        || trimmed.StartsWith("FN;", StringComparison.OrdinalIgnoreCase))
      {
        currentName = ExtractFieldValue(trimmed);
      }
      else if (trimmed.StartsWith("TEL:", StringComparison.OrdinalIgnoreCase)
        || trimmed.StartsWith("TEL;", StringComparison.OrdinalIgnoreCase))
      {
        var raw = ExtractFieldValue(trimmed);
        var normalized = PhoneNumberNormalizer.Normalize(raw);
        if (!string.IsNullOrEmpty(normalized))
          currentNumbers.Add(normalized);
      }
    }

    return contacts;
  }

  private static string ExtractFieldValue(string line)
  {
    // Format: FIELD;PARAMS:VALUE or FIELD:VALUE
    var colonIdx = line.IndexOf(':');
    if (colonIdx < 0) return string.Empty;

    var value = line[(colonIdx + 1)..].Trim();
    var fieldPart = line[..colonIdx].ToUpperInvariant();

    // Check for QUOTED-PRINTABLE encoding
    if (fieldPart.Contains("ENCODING=QUOTED-PRINTABLE"))
    {
      value = DecodeQuotedPrintable(value);
    }

    return value;
  }

  private static string DecodeQuotedPrintable(string input)
  {
    var bytes = new List<byte>();
    var i = 0;
    while (i < input.Length)
    {
      if (input[i] == '=' && i + 2 < input.Length)
      {
        var hex = input.Substring(i + 1, 2);
        if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
          bytes.Add(b);
          i += 3;
          continue;
        }
      }
      bytes.Add((byte)input[i]);
      i++;
    }
    return Encoding.UTF8.GetString(bytes.ToArray());
  }
}
