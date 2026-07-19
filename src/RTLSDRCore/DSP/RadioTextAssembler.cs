namespace RTLSDRCore.DSP;

/// <summary>
/// Assembles RDS RadioText (RT, group 2A/2B) segments into confirmed messages
/// with per-character noise rejection and complete-before-partial publishing.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="RdsDecoder"/> so the RT state machine is testable
/// without driving the full DSP chain. Two hardening rules replace the prior
/// "any ≥4-char contiguous prefix seen twice" confirmation, both motivated by
/// production console logs where the old policy published truncated prefixes
/// (<c>"Simo"</c> followed by the full text ~5 s later) and CRC-aliased
/// corruption (<c>"GivJ It Away"</c>, <c>"MaIonna"</c>) that was then
/// re-published corrected — every one of which appended a garbage chunk into
/// the UI's accumulating RT ticker:
/// </para>
/// <para>
/// 1. <b>Per-character double-receive.</b> A character slot only enters the
/// assembly once the same value has been decoded for that slot twice. The RDS
/// 10-bit block CRC aliases under burst errors, so occasionally a corrupt
/// block passes the syndrome check; requiring two sightings of the same value
/// makes a one-off corrupt character effectively impossible to land (the next
/// segment cycle re-sends the true value, which then wins). This mirrors what
/// hardened decoders (e.g. redsea) do.
/// </para>
/// <para>
/// 2. <b>Complete-before-partial confirmation.</b> A COMPLETE message — the
/// full 64 characters received, or a 0x0D terminator observed (which pads the
/// remainder with spaces) — confirms after
/// <see cref="CompleteConfirmThreshold"/> consecutive stable assemblies, same
/// as before. An INCOMPLETE prefix must instead stay byte-stable for
/// <see cref="PartialConfirmThreshold"/> consecutive RT groups (≈ two full
/// 16-segment cycles) before it may confirm. A transiently-missing segment is
/// virtually always repaired within one cycle — which grows the text and
/// resets the stability counter — so reception gaps no longer publish
/// truncated prefixes. Stations with broken encoders (no terminator, not all
/// segments transmitted) still display after ~10–30 s of genuine stability.
/// </para>
/// </remarks>
internal sealed class RadioTextAssembler
{
  /// <summary>RT messages are at most 64 characters (Group 2A).</summary>
  private const int RtLength = 64;

  /// <summary>
  /// Stable assemblies required to confirm a complete (64-char / terminated)
  /// message. Kept at the historical value — completeness plus per-char
  /// double-receive already provides the noise rejection.
  /// </summary>
  internal const int CompleteConfirmThreshold = 2;

  /// <summary>
  /// Stable assemblies required to confirm an incomplete prefix. RT groups
  /// arrive at ~1–3/s and a full 16-segment cycle is 16 groups, so 32 ≈ two
  /// cycles of zero growth — strong evidence the station genuinely stops
  /// there rather than us having missed a segment.
  /// </summary>
  internal const int PartialConfirmThreshold = 32;

  // Accepted characters — value seen twice for the slot. These are what the
  // assembly is built from.
  private readonly char[] _accepted = new char[RtLength];
  private readonly bool[] _acceptedValid = new bool[RtLength];

  // Most recent single sighting per slot — the candidate for acceptance.
  private readonly char[] _staged = new char[RtLength];
  private readonly bool[] _stagedValid = new bool[RtLength];

  private bool _abFlag;             // A/B flag — toggles on new message
  private bool _abFlagInitialized;
  private string? _candidate;       // assembled text awaiting confirmation
  private int _candidateMatchCount; // consecutive identical assemblies

  /// <summary>
  /// The most recently confirmed RadioText (trimmed), or null when nothing
  /// has been confirmed since the last <see cref="Reset"/>. Retained across
  /// A/B toggles until the next message confirms, matching receiver
  /// convention (the display keeps showing the old text while the new one
  /// assembles).
  /// </summary>
  public string? ConfirmedText { get; private set; }

  /// <summary>
  /// Feed one group 2A/2B worth of RT segment data.
  /// </summary>
  /// <returns>
  /// True when this group caused a NEW text to be confirmed (the caller logs
  /// exactly once per distinct confirmation).
  /// </returns>
  public bool ProcessGroup(ushort blockB, ushort blockC, ushort blockD, bool versionB)
  {
    // A/B flag in bit 4 of block B — toggles when the station starts a new
    // message. Clear the whole assembly INCLUDING the stability candidate so
    // the outgoing message can't bleed into the incoming one.
    var abFlag = ((blockB >> 4) & 0x01) == 1;
    if (_abFlagInitialized && abFlag != _abFlag)
    {
      ClearAssemblyState();
    }
    _abFlag = abFlag;
    _abFlagInitialized = true;

    // Block B bits 3-0: text segment address.
    var segmentAddr = blockB & 0x0F;

    if (versionB)
    {
      // Group 2B: 2 chars from block D only (block C carries the PI repeat).
      var pos = segmentAddr * 2;
      if (pos + 1 < RtLength)
      {
        ReceiveChar(pos, (char)((blockD >> 8) & 0xFF));
        ReceiveChar(pos + 1, (char)(blockD & 0xFF));
      }
    }
    else
    {
      // Group 2A: 4 chars from blocks C and D.
      var pos = segmentAddr * 4;
      if (pos + 3 < RtLength)
      {
        ReceiveChar(pos, (char)((blockC >> 8) & 0xFF));
        ReceiveChar(pos + 1, (char)(blockC & 0xFF));
        ReceiveChar(pos + 2, (char)((blockD >> 8) & 0xFF));
        ReceiveChar(pos + 3, (char)(blockD & 0xFF));
      }
    }

    return TryConfirm();
  }

  /// <summary>
  /// Full reset — used when the receiver retunes. Clears the assembly, the
  /// A/B tracker, and the confirmed text.
  /// </summary>
  public void Reset()
  {
    ClearAssemblyState();
    _abFlag = false;
    _abFlagInitialized = false;
    ConfirmedText = null;
  }

  private void ClearAssemblyState()
  {
    Array.Clear(_accepted);
    Array.Clear(_acceptedValid);
    Array.Clear(_staged);
    Array.Clear(_stagedValid);
    _candidate = null;
    _candidateMatchCount = 0;
  }

  private void ReceiveChar(int pos, char c)
  {
    // 0x0D (carriage return) terminates the message: everything from its
    // position to the end is padding. The terminator itself goes through the
    // same double-receive rule — a corrupt byte aliasing to 0x0D would
    // otherwise wipe the tail of a longer message.
    if (c == '\r')
    {
      if (_stagedValid[pos] && _staged[pos] == '\r')
      {
        for (var i = pos; i < RtLength; i++)
        {
          _accepted[i] = ' ';
          _acceptedValid[i] = true;
        }
      }
      else
      {
        _staged[pos] = '\r';
        _stagedValid[pos] = true;
      }
      return;
    }

    // Only printable ASCII participates; anything else is dropped on the
    // floor (same validation the decoder always applied).
    if (c < 0x20 || c > 0x7E)
    {
      return;
    }

    if (_acceptedValid[pos] && _accepted[pos] == c)
    {
      // Re-confirmation of an already-accepted value — refresh the stage so
      // a later corrupt sighting has to repeat twice to displace it.
      _staged[pos] = c;
      _stagedValid[pos] = true;
      return;
    }

    if (_stagedValid[pos] && _staged[pos] == c)
    {
      // Second consecutive sighting of the same value → accept (this also
      // REPLACES a previously-accepted value, which is how in-place text
      // changes without an A/B toggle eventually propagate).
      _accepted[pos] = c;
      _acceptedValid[pos] = true;
      return;
    }

    // First sighting of a new value for this slot — stage it.
    _staged[pos] = c;
    _stagedValid[pos] = true;
  }

  private bool TryConfirm()
  {
    // Contiguous accepted run from position 0.
    var length = 0;
    for (var i = 0; i < RtLength; i++)
    {
      if (!_acceptedValid[i])
      {
        break;
      }
      length = i + 1;
    }

    // Need at least 4 characters to be meaningful.
    if (length < 4)
    {
      return false;
    }

    var text = new string(_accepted, 0, length).Trim();
    if (string.IsNullOrEmpty(text))
    {
      return false;
    }

    if (text == _candidate)
    {
      _candidateMatchCount++;
      var threshold = length == RtLength ? CompleteConfirmThreshold : PartialConfirmThreshold;
      if (_candidateMatchCount >= threshold && ConfirmedText != text)
      {
        ConfirmedText = text;
        return true;
      }
    }
    else
    {
      _candidate = text;
      _candidateMatchCount = 1;
    }

    return false;
  }
}
