namespace Radio.Infrastructure.Platform.Bluetooth.Native;

/// <summary>
/// Pure filter logic for PipeWire BT capture node names. Extracted from
/// <c>PipeWireRegistryListener</c> so the (parser-level) classification can
/// be unit-tested without a real PipeWire daemon. The listener calls
/// <see cref="TryExtractBtCaptureAddress"/> from its <c>global()</c>
/// callback to decide whether a newly-appeared global is a BT capture node
/// and, if so, the device address it represents.
/// </summary>
/// <remarks>
/// Recognises <c>bluez_input.AA_BB_CC_DD_EE_FF</c> followed by either the end of the
/// string or a <c>.</c> and a non-empty suffix — the same shape the working
/// <c>pw-cli</c> scrape has always matched by prefix
/// (<c>LinuxBluetoothService.FindPipeWireBluetoothNodeAsync</c>).
///
/// ⚠ AUD-10: this filter used to require the suffix <c>.a2dp-source</c>, a naming
/// convention the appliance (PipeWire 1.0.7) never produces. The node there is
/// <c>bluez_input.B0_D5_FB_D2_0D_68.2</c> — measured 2026-09-10 and 2026-09-25, <c>.2</c>
/// both times. So the filter never matched, <c>NodeAppeared</c>/<c>NodeDisappeared</c>
/// never fired in production, and the listener's healthy start-up report switched off
/// the Plan B fallback scrape at the same time. Its tests passed because their fixtures
/// were invented in the same shape as the filter. ⛔ Do not match the full name or pin the
/// numeric suffix: two sightings of <c>.2</c> are not a guarantee (plan AUD-10 §0.3).
///
/// ⚠ The literal suffixes <c>.hfp-ag</c> / <c>.hfp-hf</c> are rejected, but that is NOT a profile
/// filter and must not be read as one: no source here shows any PipeWire/WirePlumber version naming
/// nodes that way, and WirePlumber may well name every profile's node numerically
/// (<c>bluez_input.&lt;MAC&gt;.&lt;N&gt;</c>), in which case an HFP node for the same phone passes this
/// filter exactly like the A2DP one. Not observed: WirePlumber only manages <c>hci0</c> on the
/// appliance and the HFP path lives on the second adapter (RotaryPhone, CLAUDE.md cross-service
/// boundary). What bounds the blast radius downstream is that a removal tears down only a stream bound
/// to the removed node's serial, and a re-bind targets only the connected device. If profile
/// discrimination is ever needed, read <c>api.bluez5.profile</c> / <c>media.class</c> from the global's
/// properties — not the name.
/// </remarks>
internal static class PipeWireRegistryFilter
{
  private const string Prefix = "bluez_input.";

  // Underscored MAC body length: "AA_BB_CC_DD_EE_FF" = 17 chars.
  private const int MacBodyLength = 17;

  private static readonly string[] RejectedSuffixes = { "hfp-ag", "hfp-hf" };

  /// <summary>
  /// Returns true if the given PipeWire node name is a BT capture node
  /// (<c>bluez_input.&lt;MAC&gt;</c> or <c>bluez_input.&lt;MAC&gt;.&lt;suffix&gt;</c>),
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

    var bodyStart = Prefix.Length;
    var bodyEnd = bodyStart + MacBodyLength;
    if (nodeName.Length < bodyEnd)
    {
      return false;
    }

    // After the MAC body: either end-of-string, or '.' followed by a non-empty suffix.
    // Anything else (a longer MAC-like body, a '-' separator, ...) is not our shape.
    if (nodeName.Length > bodyEnd)
    {
      if (nodeName[bodyEnd] != '.')
      {
        return false;
      }
      var suffix = nodeName[(bodyEnd + 1)..];
      if (suffix.Length == 0)
      {
        return false;
      }
      foreach (var rejected in RejectedSuffixes)
      {
        if (string.Equals(suffix, rejected, System.StringComparison.Ordinal))
        {
          return false;
        }
      }
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
