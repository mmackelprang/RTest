using RTLSDRCore.DSP;
using Xunit;

namespace RTLSDRCore.Tests;

/// <summary>
/// Unit tests for <see cref="RadioTextAssembler"/> — the RDS RadioText segment
/// assembler behind <c>RdsDecoder.RadioText</c>.
///
/// The scenarios mirror the failure modes observed in production console logs
/// (radio-20260717.txt) under the previous inline assembly:
///   - "Simo" published, then the full text ~5 s later (partial-prefix publish
///     when a segment was lost in the first cycle)
///   - "GivJ It Away" / "MaIonna" / "BoGEMarl" published, then re-published
///     corrected (CRC-aliased corrupt block confirmed by the old
///     2-consecutive-assemblies threshold)
///
/// Policy under test:
///   - Per-character double-receive (a slot's value must be decoded twice)
///   - Complete messages (64 chars via full reception or 0x0D fill) confirm
///     after CompleteConfirmThreshold stable assemblies
///   - Incomplete prefixes need PartialConfirmThreshold consecutive stable
///     assemblies (≈ two full segment cycles) — broken-encoder stations still
///     display, transient reception gaps do not publish truncated prefixes
/// </summary>
public class RadioTextAssemblerTests
{
  /// <summary>
  /// Builds the group 2A block sequence for one full transmission cycle of
  /// <paramref name="message"/>: the message characters, a 0x0D terminator
  /// (unless <paramref name="terminated"/> is false), transmitted as 4-char
  /// segments. Real stations only transmit the segments that carry content —
  /// the terminator fill covers the rest.
  /// </summary>
  private static List<(ushort BlockB, ushort BlockC, ushort BlockD)> BuildCycle2A(
    string message, bool abFlag, bool terminated = true, int? skipSegment = null,
    (int Segment, int CharOffset, char Corrupt)? corruption = null)
  {
    var payload = terminated ? message + "\r" : message;
    // Pad to a whole 4-char segment with spaces (stations pad the final
    // segment; padding after 0x0D is ignored by the assembler anyway).
    while (payload.Length % 4 != 0)
    {
      payload += " ";
    }

    var groups = new List<(ushort, ushort, ushort)>();
    var segmentCount = payload.Length / 4;
    for (var seg = 0; seg < segmentCount; seg++)
    {
      if (seg == skipSegment)
      {
        continue;
      }

      var chars = payload.Substring(seg * 4, 4).ToCharArray();
      if (corruption is { } c && c.Segment == seg)
      {
        chars[c.CharOffset] = c.Corrupt;
      }

      // Group type 2 (bits 15-12), version A (bit 11 = 0), A/B flag bit 4,
      // segment address bits 3-0.
      var blockB = (ushort)(0x2000 | (abFlag ? 0x10 : 0x00) | (seg & 0x0F));
      var blockC = (ushort)((chars[0] << 8) | chars[1]);
      var blockD = (ushort)((chars[2] << 8) | chars[3]);
      groups.Add((blockB, blockC, blockD));
    }

    return groups;
  }

  /// <summary>
  /// Feeds the given cycles into the assembler, collecting every confirmed
  /// text in order (ProcessGroup returns true exactly once per confirmation).
  /// </summary>
  private static List<string> Feed(
    RadioTextAssembler assembler,
    params List<(ushort BlockB, ushort BlockC, ushort BlockD)>[] cycles)
  {
    var confirmed = new List<string>();
    foreach (var cycle in cycles)
    {
      foreach (var (b, c, d) in cycle)
      {
        if (assembler.ProcessGroup(b, c, d, versionB: false))
        {
          confirmed.Add(assembler.ConfirmedText!);
        }
      }
    }
    return confirmed;
  }

  private const string Message = "Simon - Madonna :: Material Girl";

  // ─── Clean reception ─────────────────────────────────────────────────────

  [Fact]
  public void CleanSignal_ConfirmsCompleteTextExactlyOnce_NoPartials()
  {
    var assembler = new RadioTextAssembler();
    var cycle = BuildCycle2A(Message, abFlag: false);

    // Three full cycles — plenty for double-receive + stability confirmation.
    var confirmed = Feed(assembler, cycle, cycle, cycle);

    // A clean signal must confirm the complete message exactly once — the old
    // assembly's partial prefixes ("Simo") must never publish.
    Assert.Equal(new[] { Message }, confirmed);
    Assert.Equal(Message, assembler.ConfirmedText);
  }

  [Fact]
  public void CleanSignal_NeedsTwoSightingsPerChar_NothingConfirmedAfterOneCycle()
  {
    var assembler = new RadioTextAssembler();
    var cycle = BuildCycle2A(Message, abFlag: false);

    var confirmed = Feed(assembler, cycle);

    // Per-char double-receive: a single cycle stages every slot, accepts none.
    Assert.Empty(confirmed);
    Assert.Null(assembler.ConfirmedText);
  }

  // ─── Reception gaps (the "Simo" bug) ─────────────────────────────────────

  [Fact]
  public void MissingSegment_RepairedNextCycle_NeverConfirmsPartialPrefix()
  {
    var assembler = new RadioTextAssembler();
    var lossy = BuildCycle2A(Message, abFlag: false, skipSegment: 1);
    var clean = BuildCycle2A(Message, abFlag: false);

    var confirmed = Feed(assembler, lossy, clean, clean, clean);

    // A transiently-missing segment must not publish the stalled prefix — the
    // repair next cycle grows the text before the partial threshold is reached.
    Assert.Equal(new[] { Message }, confirmed);
  }

  [Fact]
  public void SameSegmentMissingTwoCyclesRunning_StillNoPartial_WhenRepairedWithinThreshold()
  {
    var assembler = new RadioTextAssembler();
    var lossy = BuildCycle2A(Message, abFlag: false, skipSegment: 3);
    var clean = BuildCycle2A(Message, abFlag: false);

    // Segment 3 missing in cycles 1 AND 2 — the stalled 12-char prefix
    // stabilises and accrues stability counts across cycles 2-3, but the
    // repair (second sighting of segment 3 in cycle 4) grows the text before
    // the 32-assembly partial threshold is reached.
    var confirmed = Feed(assembler, lossy, lossy, clean, clean, clean);

    Assert.All(confirmed, t => Assert.Equal(Message, t));
    Assert.Single(confirmed);
  }

  // ─── Corruption (the "GivJ It Away" bug) ─────────────────────────────────

  [Fact]
  public void OneOffCorruptChar_NeverConfirmed()
  {
    var assembler = new RadioTextAssembler();
    var clean = BuildCycle2A(Message, abFlag: false);
    // Cycle 3 delivers segment 2 with one corrupt char ('I' where 'd' should
    // be — modelled on the production "MaIonna" event).
    var corrupt = BuildCycle2A(Message, abFlag: false, corruption: (2, 1, 'I'));

    var confirmed = Feed(assembler, clean, clean, corrupt, clean, clean);

    // A CRC-aliased corrupt block seen once must never displace an accepted
    // character — the old assembly published "MaIonna" variants.
    Assert.All(confirmed, t => Assert.Equal(Message, t));
    Assert.Single(confirmed);
  }

  [Fact]
  public void CorruptCharBeforeFirstConfirmation_DelaysButDoesNotCorrupt()
  {
    var assembler = new RadioTextAssembler();
    var corrupt = BuildCycle2A(Message, abFlag: false, corruption: (1, 0, 'X'));
    var clean = BuildCycle2A(Message, abFlag: false);

    var confirmed = Feed(assembler, corrupt, clean, clean, clean);

    Assert.Equal(new[] { Message }, confirmed);
  }

  // ─── Broken-encoder stations (partial threshold path) ────────────────────

  [Fact]
  public void UnterminatedShortMessage_ConfirmsAfterPartialThreshold()
  {
    // A station that transmits only the segments carrying its short text and
    // never sends a 0x0D terminator: the assembly can never reach 64 chars,
    // so the partial-stability path is its only route to display.
    var assembler = new RadioTextAssembler();
    const string shortText = "WKRP ROCK 92"; // 12 chars → 3 segments
    var cycle = BuildCycle2A(shortText, abFlag: false, terminated: false);

    var confirmed = new List<string>();
    // 3 groups per cycle → 40 cycles = 120 groups, comfortably past the
    // 32-stable-assembly partial threshold once double-receive completes.
    for (var i = 0; i < 40; i++)
    {
      foreach (var (b, c, d) in cycle)
      {
        if (assembler.ProcessGroup(b, c, d, versionB: false))
        {
          confirmed.Add(assembler.ConfirmedText!);
        }
      }
    }

    // Genuinely-stable partial text (broken encoder, no terminator) must still
    // display after the high stability threshold.
    Assert.Equal(new[] { shortText }, confirmed);
  }

  [Fact]
  public void UnterminatedShortMessage_NotConfirmedWithinTwoCycles()
  {
    var assembler = new RadioTextAssembler();
    const string shortText = "WKRP ROCK 92";
    var cycle = BuildCycle2A(shortText, abFlag: false, terminated: false);

    var confirmed = Feed(assembler, cycle, cycle);

    // An incomplete prefix must not confirm at the complete-message threshold —
    // that is exactly the old partial-publish bug.
    Assert.Empty(confirmed);
  }

  // ─── A/B flag (message rotation) ─────────────────────────────────────────

  [Fact]
  public void AbFlagToggle_ClearsAssembly_NextMessageConfirmsCleanly()
  {
    var assembler = new RadioTextAssembler();
    const string first = "Simon - Men Without Hats :: The Safety Dance";
    const string second = "Simon Says, It's The Weekend!";

    var cycleA = BuildCycle2A(first, abFlag: false);
    var cycleB = BuildCycle2A(second, abFlag: true);

    var confirmed = Feed(assembler, cycleA, cycleA, cycleB, cycleB, cycleB);

    // Each rotation message confirms once; no mixed-message hybrids may confirm.
    Assert.Equal(new[] { first, second }, confirmed);
  }

  [Fact]
  public void AbFlagToggle_KeepsPriorConfirmedTextUntilNewOneConfirms()
  {
    var assembler = new RadioTextAssembler();
    const string first = "Simon - Bon Jovi :: Wanted Dead Or Alive";
    var cycleA = BuildCycle2A(first, abFlag: false);
    Feed(assembler, cycleA, cycleA, cycleA);
    Assert.Equal(first, assembler.ConfirmedText);

    // New message begins (A/B toggles) — one cycle in, nothing new confirmed
    // yet, and the display keeps showing the prior text (receiver convention).
    var cycleB = BuildCycle2A("Simon - Madonna :: Material Girl", abFlag: true);
    Feed(assembler, cycleB);

    Assert.Equal(first, assembler.ConfirmedText);
  }

  [Fact]
  public void OneOffCorruptAbFlagBit_DoesNotClearAssembly()
  {
    // Block B is subject to the same CRC-alias risk as the character blocks:
    // a single group with a flipped A/B bit (flag toggled, otherwise valid)
    // must NOT wipe the in-progress assembly/candidate — the toggle needs a
    // second consecutive sighting to commit (pre-merge review finding #3).
    var assembler = new RadioTextAssembler();
    var clean = BuildCycle2A(Message, abFlag: false);
    Feed(assembler, clean); // cycle 1 — every slot staged, none accepted yet

    // One corrupt group mid-stream: segment 5's block B with the A/B bit
    // flipped (otherwise valid).
    var corrupt = clean[5];
    var flippedB = (ushort)(corrupt.BlockB ^ 0x10);
    assembler.ProcessGroup(flippedB, corrupt.BlockC, corrupt.BlockD, versionB: false);

    // With the staging intact, cycle 2 completes double-receive and the
    // complete message confirms at the end of this cycle. The old
    // clear-on-single-sighting behaviour wiped the staged cycle-1 work here,
    // pushing confirmation out by two more full cycles — this assertion
    // fails under that behaviour.
    var confirmed = Feed(assembler, clean);
    Assert.Equal(new[] { Message }, confirmed);
  }

  [Fact]
  public void GenuineAbToggle_CommitsOnSecondSighting_AndStillConfirmsNewMessage()
  {
    // The double-receive on the flag drops the first group of the new
    // message; the segment cycle re-sends it, so the new message still
    // assembles and confirms within a few cycles.
    var assembler = new RadioTextAssembler();
    const string first = "Simon - Blondie :: Call Me";
    const string second = "Simon - Toto :: Africa";
    var cycleA = BuildCycle2A(first, abFlag: false);
    var cycleB = BuildCycle2A(second, abFlag: true);

    // Three cycleA: `first` is not 4-char-aligned, so its complete-text
    // candidate is set one group later than an aligned message's and needs
    // the start of cycle 3 to reach the confirm threshold.
    var confirmed = Feed(assembler, cycleA, cycleA, cycleA, cycleB, cycleB, cycleB, cycleB);

    Assert.Equal(new[] { first, second }, confirmed);
  }

  // ─── Group 2B (2-char segments) ──────────────────────────────────────────

  [Fact]
  public void Version2B_TerminatedMessage_ConfirmsComplete()
  {
    var assembler = new RadioTextAssembler();
    const string message = "ROCK 92 FM"; // 10 chars + \r → 6 2B segments
    var payload = message + "\r";
    while (payload.Length % 2 != 0)
    {
      payload += " ";
    }

    var confirmed = new List<string>();
    for (var cycle = 0; cycle < 4; cycle++)
    {
      for (var seg = 0; seg < payload.Length / 2; seg++)
      {
        var blockB = (ushort)(0x2800 | (seg & 0x0F)); // type 2, version B
        var blockD = (ushort)((payload[seg * 2] << 8) | payload[seg * 2 + 1]);
        if (assembler.ProcessGroup(blockB, blockC: 0x0000, blockD, versionB: true))
        {
          confirmed.Add(assembler.ConfirmedText!);
        }
      }
    }

    Assert.Equal(new[] { message }, confirmed);
  }

  // ─── Reset ───────────────────────────────────────────────────────────────

  [Fact]
  public void Reset_ClearsConfirmedTextAndAssembly()
  {
    var assembler = new RadioTextAssembler();
    var cycle = BuildCycle2A(Message, abFlag: false);
    Feed(assembler, cycle, cycle, cycle);
    Assert.Equal(Message, assembler.ConfirmedText);

    assembler.Reset();

    Assert.Null(assembler.ConfirmedText);

    // Post-reset the assembler behaves like new — double-receive starts over.
    Feed(assembler, cycle);
    Assert.Null(assembler.ConfirmedText);
    Feed(assembler, cycle, cycle);
    Assert.Equal(Message, assembler.ConfirmedText);
  }
}
