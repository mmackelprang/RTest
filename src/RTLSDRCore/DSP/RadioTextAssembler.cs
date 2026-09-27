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
/// 2. <b>Complete-before-partial confirmation.</b> A COMPLETE message — every
/// slot the group version can address filled (64 for 2A, 32 for 2B), or a
/// 0x0D terminator observed (which pads the remainder with spaces) — confirms
/// after <see cref="CompleteConfirmThreshold"/> consecutive stable assemblies.
/// An INCOMPLETE prefix must instead stay byte-stable for
/// <see cref="PartialConfirmThreshold"/> consecutive RT groups (≈ two full
/// 16-segment cycles) before it may confirm. A transiently-missing segment is
/// virtually always repaired within one cycle — which grows the text and
/// resets the stability counter — so reception gaps no longer publish
/// truncated prefixes. Stations with broken encoders (no terminator, not all
/// segments transmitted) still display after ~10–30 s of genuine stability.
/// </para>
/// <para>
/// 3. <b>Change waves</b> (<c>AUD-70</c>). Many stations change the RT in
/// place — same A/B flag, new characters — so the old message's accepted
/// characters are replaced slot by slot as the new one is double-received.
/// Rule 2 alone called that assembly complete as soon as every slot held
/// <i>some</i> accepted value, so one lost group during the changeover left
/// four characters of the old message in an otherwise-new one and confirmed
/// the hybrid (<c>"Toto - Afrinna :: Material Girl"</c>). Now the first
/// sighting of a value that differs from a slot's current accepted value opens
/// a new change wave, and every slot must be re-sighted (accepted, or
/// re-confirmed unchanged) within the current wave before the assembly counts
/// as complete. A slot the changeover has not reached yet is not stale text to
/// publish; it is a gap, and the assembly waits for it exactly as it waits for
/// a never-received slot.
/// </para>
/// </remarks>
internal sealed class RadioTextAssembler
{
  /// <summary>RT messages are at most 64 characters (Group 2A).</summary>
  private const int RtLength = 64;

  /// <summary>Group 2B segments carry two characters, so 2B messages are at most 32.</summary>
  private const int RtLengthVersionB = 32;

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

  // The change wave in which each accepted slot was last sighted (accepted,
  // re-confirmed, or covered by a terminator fill). A slot whose wave is
  // older than _changeWave has not been seen since the current changeover
  // began and does not count towards a complete assembly.
  private readonly int[] _slotWave = new int[RtLength];
  private int _changeWave = 1;

  // Most recent single sighting per slot — the candidate for acceptance.
  private readonly char[] _staged = new char[RtLength];
  private readonly bool[] _stagedValid = new bool[RtLength];

  private bool _abFlag;             // A/B flag — toggles on new message
  private bool _abFlagInitialized;
  private bool _abFlagTogglePending; // one group carried the opposite flag
  private string? _candidate;       // assembled text awaiting confirmation
  private int _candidateMatchCount; // consecutive identical assemblies
  private int _messageCapacity = RtLength; // 64 for 2A, 32 for 2B (per the last group)

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
    // message. Block B is subject to the same CRC-alias risk as the character
    // blocks, so the toggle gets the same double-receive treatment: the FIRST
    // group carrying the opposite flag is treated as suspect (its characters
    // are not written anywhere); only a SECOND consecutive opposite-flag group
    // commits the toggle and clears the assembly INCLUDING the stability
    // candidate, so the outgoing message can't bleed into the incoming one.
    // A one-off corrupt flag bit therefore can no longer wipe an in-progress
    // candidate (pre-merge review finding #3).
    var abFlag = ((blockB >> 4) & 0x01) == 1;
    if (!_abFlagInitialized)
    {
      _abFlag = abFlag;
      _abFlagInitialized = true;
    }
    else if (abFlag != _abFlag)
    {
      if (!_abFlagTogglePending)
      {
        // First opposite-flag sighting — stage it, drop this group's chars.
        // If genuine, the station re-sends the segment next cycle; if it was
        // a corrupt flag bit, the group was suspect anyway.
        _abFlagTogglePending = true;
        return false;
      }

      // Second consecutive opposite-flag group — genuine new message.
      ClearAssemblyState();
      _abFlag = abFlag;
      _abFlagTogglePending = false;
    }
    else
    {
      // Flag stable/reverted — any prior single opposite sighting was noise.
      _abFlagTogglePending = false;
    }

    // Block B bits 3-0: text segment address.
    var segmentAddr = blockB & 0x0F;

    if (versionB)
    {
      // Group 2B: 2 chars from block D only (block C carries the PI repeat).
      _messageCapacity = RtLengthVersionB;
      var pos = segmentAddr * 2;
      if (pos + 1 < RtLength)
      {
        ReceiveByte(pos, (byte)((blockD >> 8) & 0xFF));
        ReceiveByte(pos + 1, (byte)(blockD & 0xFF));
      }
    }
    else
    {
      // Group 2A: 4 chars from blocks C and D.
      _messageCapacity = RtLength;
      var pos = segmentAddr * 4;
      if (pos + 3 < RtLength)
      {
        ReceiveByte(pos, (byte)((blockC >> 8) & 0xFF));
        ReceiveByte(pos + 1, (byte)(blockC & 0xFF));
        ReceiveByte(pos + 2, (byte)((blockD >> 8) & 0xFF));
        ReceiveByte(pos + 3, (byte)(blockD & 0xFF));
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
    _abFlagTogglePending = false;
    _messageCapacity = RtLength;
    ConfirmedText = null;
  }

  private void ClearAssemblyState()
  {
    Array.Clear(_accepted);
    Array.Clear(_acceptedValid);
    Array.Clear(_slotWave);
    _changeWave = 1;
    Array.Clear(_staged);
    Array.Clear(_stagedValid);
    _candidate = null;
    _candidateMatchCount = 0;
  }

  private void ReceiveByte(int pos, byte code)
  {
    // 0x0D (carriage return) terminates the message: everything from its
    // position to the end is padding. The terminator itself goes through the
    // same double-receive rule — a corrupt byte aliasing to 0x0D would
    // otherwise wipe the tail of a longer message.
    if (code == 0x0D)
    {
      if (_stagedValid[pos] && _staged[pos] == '\r')
      {
        for (var i = pos; i < RtLength; i++)
        {
          _accepted[i] = ' ';
          _acceptedValid[i] = true;
          _slotWave[i] = _changeWave;
        }
      }
      else
      {
        // A terminator where an accepted non-space character stands means the
        // message got shorter — a change in flight, like any other new value.
        if (_acceptedValid[pos] && _accepted[pos] != ' ')
        {
          OpenChangeWave(pos);
        }
        _staged[pos] = '\r';
        _stagedValid[pos] = true;
      }
      return;
    }

    char c;
    if (code == 0x0A || code == 0x0B)
    {
      // Line feed / end-of-headline: layout hints on a multi-line display.
      // On a single-line ticker they are word breaks, and dropping them (as
      // the old validation did) left the slot forever unfilled, so a message
      // containing one could never be complete.
      c = ' ';
    }
    else if (!RdsCharset.TryDecode(code, out c))
    {
      // Remaining control codes are not characters — nothing to record.
      return;
    }

    if (_acceptedValid[pos] && _accepted[pos] == c)
    {
      // Re-confirmation of an already-accepted value — refresh the stage so
      // a later corrupt sighting has to repeat twice to displace it, and mark
      // the slot as sighted in the current wave.
      _staged[pos] = c;
      _stagedValid[pos] = true;
      _slotWave[pos] = _changeWave;
      return;
    }

    if (_stagedValid[pos] && _staged[pos] == c)
    {
      // Second consecutive sighting of the same value → accept (this also
      // REPLACES a previously-accepted value, which is how in-place text
      // changes without an A/B toggle eventually propagate).
      _accepted[pos] = c;
      _acceptedValid[pos] = true;
      _slotWave[pos] = _changeWave;
      return;
    }

    // First sighting of a new value for this slot — stage it. If the slot
    // already holds an accepted value that was sighted in the current wave,
    // this is the first evidence of a changeover: open a new wave so every
    // slot has to be re-sighted before the assembly counts as complete.
    if (_acceptedValid[pos])
    {
      OpenChangeWave(pos);
    }
    _staged[pos] = c;
    _stagedValid[pos] = true;
  }

  private void OpenChangeWave(int pos)
  {
    // Only a slot that is current in the present wave can open a new one. A
    // slot that is already stale (its wave is older) belongs to a changeover
    // that is still in progress; a differing sighting there is that same
    // changeover reaching it, not a second one.
    if (_slotWave[pos] == _changeWave)
    {
      _changeWave++;
    }
  }

  private bool TryConfirm()
  {
    // Contiguous run from position 0 of slots that are accepted AND have been
    // sighted in the current change wave. A slot last sighted in an earlier
    // wave is text the changeover has not reached yet; publishing it would be
    // the old/new hybrid this class exists to prevent.
    var length = 0;
    for (var i = 0; i < RtLength; i++)
    {
      if (!_acceptedValid[i] || _slotWave[i] != _changeWave)
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
      var threshold = length >= _messageCapacity ? CompleteConfirmThreshold : PartialConfirmThreshold;
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
