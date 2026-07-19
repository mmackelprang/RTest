using FluentAssertions;
using Radio.Web.Services.Rds;

namespace Radio.Web.Tests.Services.Rds;

/// <summary>
/// Unit tests for <see cref="RdsAccumulatingScrollBuffer"/> — the client-side
/// rolling buffer that accumulates RDS RadioText chunks behind the
/// RadioControlPanel ticker.
///
/// Coverage targets (HANDOFF-rds-accumulating-scroll §6):
///   - Append semantics (separator placement, empty-buffer branch)
///   - Verbatim dedup (consecutive identical chunks no-op)
///   - Non-consecutive re-append (A → B → A allowed; that's rolling history)
///   - Overflow truncation (drops oldest chars on whole-char boundary, strips
///     leading separator fragments)
///   - Long-single-chunk truncation (chunk bigger than MaxChars replaces
///     whole buffer, keeps last MaxChars)
///   - Station-change reset triggers (band, frequency, PI — each
///     independently, plus null-PI edge cases per §6.b)
///   - Dedup tracker resets alongside the buffer
/// </summary>
public class RdsAccumulatingScrollBufferTests
{
  private const string DefaultSeparator = " • ";

  // ─── Construction + defaults ─────────────────────────────────────────────

  [Fact]
  public void NewBuffer_HasEmptyText()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.Text.Should().BeEmpty();
    buffer.MaxChars.Should().Be(256);
    buffer.Separator.Should().Be(DefaultSeparator);
  }

  [Fact]
  public void Constructor_ClampsMaxCharsToMinimumFloor()
  {
    // Sentinel for misconfiguration — even if a future config bridge drops
    // the cap below the floor, the buffer keeps its truncation semantics
    // well-defined.
    var buffer = new RdsAccumulatingScrollBuffer(2, DefaultSeparator);
    buffer.MaxChars.Should().Be(8, "the constructor clamps tiny caps so the truncation logic never goes negative");
  }

  [Fact]
  public void Constructor_NormalizesEmptySeparatorToSpace()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, string.Empty);
    buffer.Separator.Should().Be(" ", "empty separator would weld two chunks into an unreadable run");

    var nullBuffer = new RdsAccumulatingScrollBuffer(256, null!);
    nullBuffer.Separator.Should().Be(" ");
  }

  // ─── Append semantics ────────────────────────────────────────────────────

  [Fact]
  public void AppendChunk_FirstChunk_DoesNotPrefixSeparator()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("WUNC News");
    buffer.Text.Should().Be("WUNC News");
  }

  [Fact]
  public void AppendChunk_SecondChunk_InsertsSeparator()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("WUNC News");
    buffer.AppendChunk("Morning Edition");
    buffer.Text.Should().Be("WUNC News • Morning Edition");
  }

  [Fact]
  public void AppendChunk_ManyChunks_SeparatedCorrectly()
  {
    var buffer = new RdsAccumulatingScrollBuffer(512, DefaultSeparator);
    buffer.AppendChunk("A");
    buffer.AppendChunk("B");
    buffer.AppendChunk("C");
    buffer.AppendChunk("D");
    buffer.Text.Should().Be("A • B • C • D");
  }

  [Fact]
  public void AppendChunk_NullOrWhitespace_IsNoOp()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("Initial");
    buffer.AppendChunk(null);
    buffer.AppendChunk(string.Empty);
    buffer.AppendChunk("   ");
    buffer.Text.Should().Be("Initial");
  }

  [Fact]
  public void AppendChunk_TrimsWhitespace()
  {
    // Defensive — decoder already trims, but the buffer must not be brittle
    // if a future decoder change stops trimming.
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("  WUNC News  ");
    buffer.Text.Should().Be("WUNC News");
  }

  // ─── Dedup ───────────────────────────────────────────────────────────────

  [Fact]
  public void AppendChunk_VerbatimRepeat_IsDropped()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("WUNC News");
    buffer.AppendChunk("WUNC News");
    buffer.AppendChunk("WUNC News");
    buffer.Text.Should().Be("WUNC News",
      "consecutive identical chunks must dedup so a station holding the same RT for many decoder cycles doesn't grow the buffer");
  }

  [Fact]
  public void AppendChunk_RepeatedChunkAfterOthers_IsAppended()
  {
    // HANDOFF §6.a — A → B → C → A is the rolling history the user asked for.
    // Re-appending the same chunk after others have intervened IS allowed.
    var buffer = new RdsAccumulatingScrollBuffer(512, DefaultSeparator);
    buffer.AppendChunk("A");
    buffer.AppendChunk("B");
    buffer.AppendChunk("C");
    buffer.AppendChunk("A");
    buffer.Text.Should().Be("A • B • C • A");
  }

  [Fact]
  public void AppendChunk_RepeatedAfterTrimEquivalence_IsDropped()
  {
    // "WUNC" and "  WUNC  " trim to the same string — dedup should catch
    // that and not append twice.
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("WUNC");
    buffer.AppendChunk("  WUNC  ");
    buffer.Text.Should().Be("WUNC");
  }

  // ─── Overflow truncation ─────────────────────────────────────────────────

  [Fact]
  public void AppendChunk_Overflow_DropsOldestFromFront()
  {
    var buffer = new RdsAccumulatingScrollBuffer(20, DefaultSeparator);
    // "FIRST" + " • " + "SECOND" + " • " + "THIRDXYZ" = 5+3+6+3+8 = 25 > 20
    // After append: front-drop until <= 20, then strip any leading separator.
    buffer.AppendChunk("FIRST");
    buffer.AppendChunk("SECOND");
    buffer.AppendChunk("THIRDXYZ");

    buffer.Text.Length.Should().BeLessThanOrEqualTo(20);
    buffer.Text.Should().EndWith("THIRDXYZ", "the newest chunk must always win the right edge");
    buffer.Text.Should().NotStartWith(DefaultSeparator,
      "the overflow strip must never leave a leading separator at the head");
  }

  [Fact]
  public void AppendChunk_Overflow_DropsOnWholeCharBoundary()
  {
    var buffer = new RdsAccumulatingScrollBuffer(15, DefaultSeparator);
    buffer.AppendChunk("Hello world");
    buffer.AppendChunk("Goodbye");

    // The exact head of the resulting buffer is implementation-defined, but
    // the result must be a valid string with no replacement characters and
    // must end with the newest chunk.
    buffer.Text.Should().EndWith("Goodbye");
    buffer.Text.Length.Should().BeLessThanOrEqualTo(15);
    foreach (var c in buffer.Text)
    {
      char.IsLowSurrogate(c).Should().BeFalse("no orphaned low surrogates after a front-drop");
    }
  }

  [Fact]
  public void AppendChunk_ChunkLargerThanMaxChars_ReplacesEntireBufferWithTail()
  {
    var buffer = new RdsAccumulatingScrollBuffer(10, DefaultSeparator);
    buffer.AppendChunk("Old chunk");
    var longChunk = "0123456789ABCDEFGHIJ"; // 20 chars
    buffer.AppendChunk(longChunk);

    buffer.Text.Length.Should().Be(10);
    buffer.Text.Should().Be("ABCDEFGHIJ",
      "a chunk larger than MaxChars replaces the whole buffer with its last MaxChars characters (most-recent text wins)");
  }

  [Fact]
  public void AppendChunk_ManyChunksOverflowingBuffer_NewestChunkAlwaysVisible()
  {
    // Stress test — append 20 chunks into a 50-char buffer, the last one
    // must always be the right-most text in the buffer.
    var buffer = new RdsAccumulatingScrollBuffer(50, DefaultSeparator);
    for (var i = 0; i < 20; i++)
    {
      buffer.AppendChunk($"Chunk{i:D2}");
    }
    buffer.Text.Length.Should().BeLessThanOrEqualTo(50);
    buffer.Text.Should().EndWith("Chunk19");
    buffer.Text.Should().NotStartWith(DefaultSeparator);
  }

  // ─── Prefix-extension / correction replacement (RDS scroll-engine fix) ───
  // The hardened decoder should no longer emit partial prefixes or corrupt
  // variants, but the buffer keeps defense-in-depth: a chunk that extends or
  // minorly corrects the previous one replaces it in place instead of
  // appending a near-duplicate.

  [Fact]
  public void AppendChunk_ExtensionOfLastChunk_ReplacesInPlace()
  {
    // Production sequence (radio-20260717.txt): "Simo" confirmed, then the
    // complete message ~5 s later. The ticker must show ONE clean copy.
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("Earlier chunk");
    buffer.AppendChunk("Simo");
    buffer.AppendChunk("Simon - Madonna :: Material Girl");

    buffer.Text.Should().Be("Earlier chunk • Simon - Madonna :: Material Girl",
      "a chunk that extends the previous one is the completed version of the same message, not new history");
  }

  [Fact]
  public void AppendChunk_ShorterPrefixOfLastChunk_IsDroppedAsStale()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("Simon - Madonna :: Material Girl");
    buffer.AppendChunk("Simon - Madonna");

    buffer.Text.Should().Be("Simon - Madonna :: Material Girl",
      "a shorter prefix of the last chunk is a stale partial, not new content");
  }

  [Fact]
  public void AppendChunk_SameLengthMinorCorrection_ReplacesInPlace()
  {
    // Production sequence: "GivJ It Away" variant confirmed, then corrected.
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("Simon - Red Hot Chili Peppers :: GivJ It Away");
    buffer.AppendChunk("Simon - Red Hot Chili Peppers :: Give It Away");

    buffer.Text.Should().Be("Simon - Red Hot Chili Peppers :: Give It Away",
      "a same-length near-identical chunk is a corrected re-send of the same message");
  }

  [Fact]
  public void AppendChunk_SameLengthButDifferentMessage_AppendsNormally()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("Now Playing Song Alpha on ROCK92");
    buffer.AppendChunk("Weather:sunny and 75 in the city");

    buffer.Text.Should().Be("Now Playing Song Alpha on ROCK92 • Weather:sunny and 75 in the city",
      "two genuinely different messages of equal length must both be kept — only near-identical strings count as corrections");
  }

  [Fact]
  public void AppendChunk_ExtensionAfterReplacement_StillDedupsVerbatimRefire()
  {
    // Decoder re-fires the completed message after the replacement — must
    // still dedup against the replaced value.
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.AppendChunk("Simo");
    buffer.AppendChunk("Simon - Madonna :: Material Girl");
    buffer.AppendChunk("Simon - Madonna :: Material Girl");

    buffer.Text.Should().Be("Simon - Madonna :: Material Girl");
  }

  // ─── Whole-chunk eviction (clean head — the "cut off" fix) ───────────────

  [Fact]
  public void AppendChunk_Overflow_EvictsWholeChunks_HeadIsAlwaysAChunkBoundary()
  {
    // The old implementation trimmed characters mid-chunk, so once the buffer
    // reached its cap EVERY append left a cut-off fragment at the head
    // ("vard of Broken Dreams • …"). Whole-chunk eviction keeps the head
    // clean.
    var buffer = new RdsAccumulatingScrollBuffer(30, DefaultSeparator);
    buffer.AppendChunk("AAAAAAAAAA"); // 10
    buffer.AppendChunk("BBBBBBBBBB"); // 10 → joined 23
    buffer.AppendChunk("CCCCCCCCCC"); // 10 → joined 36 > 30 → evict AAAA…

    buffer.Text.Should().Be("BBBBBBBBBB • CCCCCCCCCC",
      "overflow evicts the oldest WHOLE chunk — the head must never be a mid-chunk fragment");
  }

  // ─── Long-run bound (leak check) ─────────────────────────────────────────

  [Fact]
  public void AppendChunk_HoursOfSimulatedUpdates_TextAndChunkCountStayBounded()
  {
    // ~2 h of worst-case RDS churn: a new distinct full-length chunk every
    // ~1.5 s (far faster than real stations rotate), with periodic station
    // changes. The buffer must stay within its cap the whole time — this is
    // the "no unbounded accumulation over a long-running kiosk session"
    // guarantee.
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 98_700_000, 0x8ACC);

    for (var i = 0; i < 5000; i++)
    {
      if (i % 500 == 499)
      {
        // Retune every ~500 updates.
        buffer.ResetOnTuneChange("FM", 98_700_000 + (i * 100_000), (ushort)(0x1000 + i));
      }

      buffer.AppendChunk($"Song number {i:D5} by Artist {i % 97} on ROCK 92 request line open");

      buffer.Text.Length.Should().BeLessThanOrEqualTo(buffer.MaxChars,
        $"the joined buffer must never exceed MaxChars (iteration {i})");
      buffer.ChunkCount.Should().BeLessThanOrEqualTo(buffer.MaxChars / 4,
        "chunk count is bounded because every kept chunk is at least a few chars plus separator");
    }
  }

  // ─── Station-change reset (HANDOFF §6.b) ─────────────────────────────────

  [Fact]
  public void ResetOnTuneChange_FirstCall_SeedsTrackerWithoutClearing()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("First chunk after seed");
    buffer.Text.Should().Be("First chunk after seed",
      "the first ResetOnTuneChange call only seeds the tracker — no buffer to clear yet");
  }

  [Fact]
  public void ResetOnTuneChange_BandChange_ClearsBuffer()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("FM content");

    buffer.ResetOnTuneChange("AM", 540_000, null);
    buffer.Text.Should().BeEmpty("a band change resets the buffer regardless of PI");
  }

  [Fact]
  public void ResetOnTuneChange_FrequencyChange_ClearsBuffer()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("WUNC content");

    buffer.ResetOnTuneChange("FM", 88_900_000, 0x1234);
    buffer.Text.Should().BeEmpty("a frequency change resets the buffer (in-band tune)");
  }

  [Fact]
  public void ResetOnTuneChange_FrequencyWithinEpsilon_DoesNotClear()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000.0, 0x8ACC);
    buffer.AppendChunk("Persistent content");

    // 0.0005 Hz drift — well below the 0.001 Hz epsilon, must NOT clear.
    buffer.ResetOnTuneChange("FM", 101_500_000.0005, 0x8ACC);
    buffer.Text.Should().Be("Persistent content",
      "floating-point round-trip jitter under 0.001 Hz must not trigger a reset");
  }

  [Fact]
  public void ResetOnTuneChange_PiChange_ClearsBuffer()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("Station A content");

    // Same band, same frequency, different PI — must clear (sub-channel /
    // station identity changed even though the dial didn't move).
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x9999);
    buffer.Text.Should().BeEmpty();
  }

  [Fact]
  public void ResetOnTuneChange_PiNullToNonNull_ClearsBuffer()
  {
    // Edge case: a station that initially had no PI lock acquires a real PI.
    // Treat that as a station identity transition — it's the strongest signal
    // RDS provides that the receiver has actually found a different station.
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, null);
    buffer.AppendChunk("Pre-lock content");

    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.Text.Should().BeEmpty();
  }

  [Fact]
  public void ResetOnTuneChange_TransientNullPi_DoesNotClear()
  {
    // HANDOFF §6.b — "a transient null-during-tune doesn't count".
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("WUNC content");

    // PI temporarily lost (decoder dropped lock), but band/frequency unchanged.
    buffer.ResetOnTuneChange("FM", 101_500_000, null);
    buffer.Text.Should().Be("WUNC content",
      "a transient null PI on the same band/frequency must not clear — the station hasn't changed");
  }

  [Fact]
  public void ResetOnTuneChange_PiReacquiresSameValue_DoesNotClear()
  {
    // HANDOFF §8 q7 — "station temporarily loses RDS lock (PI goes null),
    // then re-acquires with the same PI value — the rule correctly does NOT
    // clear." This guards the user's mental model from §8.
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("WUNC content");

    buffer.ResetOnTuneChange("FM", 101_500_000, null);    // lost lock
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);  // re-acquired SAME PI
    buffer.Text.Should().Be("WUNC content");
  }

  // ─── Dedup tracker must reset alongside the buffer ───────────────────────

  [Fact]
  public void ResetOnTuneChange_AlsoResetsDedupTracker()
  {
    // HANDOFF §6.b — "the dedup tracker MUST also reset on station change.
    // Otherwise the first RT message on the new station would be silently
    // dropped if it happened to match the last message on the prior station."
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("Coincidence");

    buffer.ResetOnTuneChange("FM", 88_900_000, 0x1234);
    // Same chunk text as before — without a dedup-tracker reset, this would
    // be silently dropped and the new station would look like it had no RT
    // at all.
    buffer.AppendChunk("Coincidence");
    buffer.Text.Should().Be("Coincidence");
  }

  // ─── Clear ───────────────────────────────────────────────────────────────

  [Fact]
  public void Clear_ResetsBufferAndTracker()
  {
    var buffer = new RdsAccumulatingScrollBuffer(256, DefaultSeparator);
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("Content");

    buffer.Clear();
    buffer.Text.Should().BeEmpty();

    // After Clear, the very next ResetOnTuneChange should behave like a
    // first-call seed (no clear), not like a station-change transition.
    buffer.ResetOnTuneChange("FM", 101_500_000, 0x8ACC);
    buffer.AppendChunk("New content");
    buffer.Text.Should().Be("New content");
  }
}
