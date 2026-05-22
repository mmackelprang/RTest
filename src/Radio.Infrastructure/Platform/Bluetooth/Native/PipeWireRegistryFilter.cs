namespace Radio.Infrastructure.Platform.Bluetooth.Native;

/// <summary>
/// Pure filter logic for PipeWire BT capture node names. Extracted from
/// <c>PipeWireRegistryListener</c> so the (parser-level) classification can
/// be unit-tested without a real PipeWire daemon. The listener calls
/// <see cref="TryExtractBtCaptureAddress"/> from its <c>global()</c>
/// callback to decide whether a newly-appeared global is a BT A2DP source
/// and, if so, the device address it represents.
///
/// Recognises names of the shape <c>bluez_input.AA_BB_CC_DD_EE_FF.a2dp-source</c>
/// (the PipeWire bluez5 module's node-naming convention for A2DP source nodes).
/// HFP profile nodes (<c>...hfp-ag</c>, <c>...hfp-hf</c>) are rejected — RTest's
/// audio path is A2DP-only; the HFP path lives on the second adapter
/// (RotaryPhone, see CLAUDE.md cross-service boundary).
/// </summary>
internal static class PipeWireRegistryFilter
{
  private const string Prefix = "bluez_input.";
  private const string A2dpSuffix = ".a2dp-source";

  // Underscored MAC body length: "AA_BB_CC_DD_EE_FF" = 17 chars.
  private const int MacBodyLength = 17;

  /// <summary>
  /// Returns true if the given PipeWire node name is a BT A2DP source node,
  /// and writes the colon-separated upper-case device address to <paramref name="address"/>.
  /// </summary>
  public static bool TryExtractBtCaptureAddress(string nodeName, out string address)
  {
    address = string.Empty;
    if (string.IsNullOrEmpty(nodeName))
    {
      return false;
    }
    if (!nodeName.StartsWith(Prefix, System.StringComparison.Ordinal))
    {
      return false;
    }
    if (!nodeName.EndsWith(A2dpSuffix, System.StringComparison.Ordinal))
    {
      return false;
    }

    var bodyStart = Prefix.Length;
    var bodyEnd = nodeName.Length - A2dpSuffix.Length;
    if (bodyEnd - bodyStart != MacBodyLength)
    {
      return false;
    }

    // Validate the body is six pairs of hex separated by underscores.
    for (var i = 0; i < MacBodyLength; i++)
    {
      var c = nodeName[bodyStart + i];
      // Positions 2, 5, 8, 11, 14 are separators (0-indexed within body).
      var isSeparator = i is 2 or 5 or 8 or 11 or 14;
      if (isSeparator)
      {
        if (c != '_')
        {
          return false;
        }
      }
      else if (!IsHex(c))
      {
        return false;
      }
    }

    address = nodeName.Substring(bodyStart, MacBodyLength)
      .Replace('_', ':')
      .ToUpperInvariant();
    return true;
  }

  private static bool IsHex(char c)
  {
    return c is >= '0' and <= '9'
        or >= 'a' and <= 'f'
        or >= 'A' and <= 'F';
  }
}
