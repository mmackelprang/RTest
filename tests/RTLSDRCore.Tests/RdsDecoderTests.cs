using RTLSDRCore.DSP;
using Xunit;

namespace RTLSDRCore.Tests;

public class RdsDecoderTests
{
  private const int SampleRate = 240000;
  private const float PilotFrequency = 19000f;
  private const float TwoPi = 2.0f * MathF.PI;

  // RDS CRC polynomial and offset words
  private const ushort CrcPoly = 0x5B9;
  private static readonly ushort[] OffsetWords = { 0x0FC, 0x198, 0x168, 0x1B4 };

  #region CRC Tests

  [Fact]
  public void ComputeSyndrome_ZeroInput_ReturnsZero()
  {
    // All-zero 26-bit word should have zero syndrome
    var syndrome = RdsDecoder.ComputeSyndrome(0);
    Assert.Equal(0, syndrome);
  }

  [Fact]
  public void CheckSyndrome_ValidBlockA_ReturnsTrue()
  {
    // Construct a valid block A: 16-bit data + 10-bit CRC with offset A applied
    var dataWord = (ushort)0x1234;
    var word26 = BuildValidBlock(dataWord, 0); // block A

    Assert.True(RdsDecoder.CheckSyndrome(word26, 0));
  }

  [Fact]
  public void CheckSyndrome_ValidBlockB_ReturnsTrue()
  {
    var dataWord = (ushort)0x5678;
    var word26 = BuildValidBlock(dataWord, 1);

    Assert.True(RdsDecoder.CheckSyndrome(word26, 1));
  }

  [Fact]
  public void CheckSyndrome_ValidBlockC_ReturnsTrue()
  {
    var dataWord = (ushort)0x9ABC;
    var word26 = BuildValidBlock(dataWord, 2);

    Assert.True(RdsDecoder.CheckSyndrome(word26, 2));
  }

  [Fact]
  public void CheckSyndrome_ValidBlockD_ReturnsTrue()
  {
    var dataWord = (ushort)0xDEF0;
    var word26 = BuildValidBlock(dataWord, 3);

    Assert.True(RdsDecoder.CheckSyndrome(word26, 3));
  }

  [Fact]
  public void CheckSyndrome_CorruptedWord_ReturnsFalse()
  {
    var dataWord = (ushort)0x1234;
    var word26 = BuildValidBlock(dataWord, 0);

    // Flip a bit
    word26 ^= 0x100;

    Assert.False(RdsDecoder.CheckSyndrome(word26, 0));
  }

  [Fact]
  public void CheckSyndrome_WrongBlockIndex_ReturnsFalse()
  {
    var dataWord = (ushort)0x1234;
    var word26 = BuildValidBlock(dataWord, 0); // built for block A

    // Check against block B — should fail
    Assert.False(RdsDecoder.CheckSyndrome(word26, 1));
  }

  #endregion

  #region Reset Tests

  [Fact]
  public void Reset_ClearsStationName()
  {
    var decoder = new RdsDecoder(SampleRate);

    // Feed a valid RDS signal to get a station name
    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "TEST FM ");

    Assert.NotNull(decoder.StationName);

    decoder.Reset();

    Assert.Null(decoder.StationName);
    Assert.False(decoder.RdsDetected);
  }

  #endregion

  #region Noise Rejection Tests

  [Fact]
  public void RandomNoise_StationNameStaysNull()
  {
    var decoder = new RdsDecoder(SampleRate);
    var random = new Random(42);
    var noise = new float[SampleRate]; // 1 second of noise

    for (int i = 0; i < noise.Length; i++)
    {
      noise[i] = (float)(random.NextDouble() * 2.0 - 1.0) * 0.1f;
    }

    // Process in blocks matching typical SDR chunk size
    const int blockSize = 4800; // ~20ms at 240kHz
    for (int offset = 0; offset < noise.Length; offset += blockSize)
    {
      var count = Math.Min(blockSize, noise.Length - offset);
      decoder.Process(noise.AsSpan(offset, count), count, 0f, PilotFrequency);
    }

    Assert.Null(decoder.StationName);
  }

  [Fact]
  public void CleanFmComposite_NoRdsSubcarrier_GracefulNull()
  {
    var decoder = new RdsDecoder(SampleRate);
    var samples = new float[SampleRate]; // 1 second

    // Generate a clean FM composite with just L+R audio and 19 kHz pilot — no RDS
    for (int i = 0; i < samples.Length; i++)
    {
      var t = (float)i / SampleRate;
      // Mono audio at 1 kHz + pilot tone
      samples[i] = 0.5f * MathF.Sin(TwoPi * 1000f * t)
                 + 0.1f * MathF.Sin(TwoPi * 19000f * t);
    }

    const int blockSize = 4800;
    for (int offset = 0; offset < samples.Length; offset += blockSize)
    {
      var count = Math.Min(blockSize, samples.Length - offset);
      decoder.Process(samples.AsSpan(offset, count), count, 0f, PilotFrequency);
    }

    Assert.Null(decoder.StationName);
  }

  #endregion

  #region PS Name Extraction Tests

  [Fact]
  public void SyntheticRdsSignal_ExtractsStationName()
  {
    var decoder = new RdsDecoder(SampleRate);
    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "KEXP-FM ");

    Assert.NotNull(decoder.StationName);
    Assert.Equal("KEXP-FM", decoder.StationName);
    Assert.True(decoder.RdsDetected);
  }

  [Fact]
  public void SyntheticRdsSignal_DifferentStationName()
  {
    var decoder = new RdsDecoder(SampleRate);
    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "KUOW    ");

    Assert.NotNull(decoder.StationName);
    Assert.Equal("KUOW", decoder.StationName);
  }

  // Task #80: the decoder must fire StationNameDecoded for each complete
  // PS frame it assembles, BEFORE its internal 2-sample confirmation.
  // Downstream consumers depend on this event firing at the underlying
  // RDS PS frame rate (~10 Hz) so their own stability filters work.
  [Fact]
  public void StationNameDecoded_Event_FiresWithDecodedName()
  {
    var decoder = new RdsDecoder(SampleRate);
    var fired = new List<string>();
    decoder.StationNameDecoded += (_, e) => fired.Add(e.Name);

    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "KEXP-FM ");

    // The synthetic signal repeats the PS name many times — we expect at
    // least one event, and every event payload should be the decoded name.
    Assert.NotEmpty(fired);
    Assert.All(fired, name => Assert.Equal("KEXP-FM", name));
  }

  // Task #80: each fully-assembled PS frame should fire the event, so a
  // long synthetic signal produces multiple events (not just one when the
  // decoder confirms internally).
  [Fact]
  public void StationNameDecoded_Event_FiresPerFrame_NotJustOnConfirmation()
  {
    var decoder = new RdsDecoder(SampleRate);
    var fireCount = 0;
    decoder.StationNameDecoded += (_, _) => fireCount++;

    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "WUNC-FM ");

    // The synthetic feed sends the PS name multiple times. The event
    // should fire more than once — that's the whole point: per-frame
    // sampling, not per-confirmation.
    Assert.True(fireCount >= 2, $"Expected at least 2 frame-level events, got {fireCount}");
  }

  #endregion


  #region PS Cycle Integrity Tests (AUD-69 / AUD-60)

  // A rolling-PS station changes all eight characters between cycles. If one
  // group of the new page is lost, the old decoder assembled a candidate from
  // the slots' last-held values — three new segments plus one old one — and,
  // because the same hybrid was re-assembled by the following segments,
  // confirmed and displayed it. A candidate must come from one complete
  // in-order cycle, so a lost segment yields no candidate at all.
  [Fact]
  public void RollingPs_PageChangeWithLostSegment_NeverEmitsHybrid()
  {
    var decoder = new RdsDecoder(SampleRate);
    var seen = new List<string>();
    decoder.StationNameDecoded += (_, e) => seen.Add(e.Name);

    const string oldPage = "ROCK 92 ";
    const string newPage = "LIMELIGH";
    var groups = new List<RdsTestGroup>();
    for (int i = 0; i < 3; i++)
    {
      groups.AddRange(RdsDecoderTestSeam.PsCycle(oldPage));
    }
    // First cycle of the new page with segment 1 lost (the group is simply
    // not transmitted, which keeps block alignment — the decoder stays synced).
    groups.Add(RdsDecoderTestSeam.PsGroup(newPage, 0));
    groups.Add(RdsDecoderTestSeam.PsGroup(newPage, 2));
    groups.Add(RdsDecoderTestSeam.PsGroup(newPage, 3));
    for (int i = 0; i < 3; i++)
    {
      groups.AddRange(RdsDecoderTestSeam.PsCycle(newPage));
    }

    RdsDecoderTestSeam.FeedSyntheticGroups(decoder, groups);

    Assert.All(seen, name => Assert.Contains(name, new[] { "ROCK 92", "LIMELIGH" }));
    Assert.Contains("LIMELIGH", seen);
    Assert.Equal("LIMELIGH", decoder.StationName);
  }

  // AUD-60: a block that fails its syndrome check used to leave the previous
  // group's word in its slot, so block D of this group was read through the
  // previous group's block B — here a stale segment-1 address puts segment
  // 2's characters into slots 2-3 and emits "RO 9 92". A group with a bad
  // block must be dropped whole.
  [Fact]
  public void BadBlockMidGroup_DropsTheGroup_NoCandidateFromStaleBlocks()
  {
    var decoder = new RdsDecoder(SampleRate);
    var seen = new List<string>();
    decoder.StationNameDecoded += (_, e) => seen.Add(e.Name);

    const string name = "ROCK 92 ";
    var groups = new List<RdsTestGroup>();
    groups.AddRange(RdsDecoderTestSeam.PsCycle(name));
    groups.AddRange(RdsDecoderTestSeam.PsCycle(name));
    groups.Add(RdsDecoderTestSeam.PsGroup(name, 0));
    groups.Add(RdsDecoderTestSeam.PsGroup(name, 1));
    groups.Add(RdsDecoderTestSeam.PsGroup(name, 2, corruptBlock: 1));
    groups.Add(RdsDecoderTestSeam.PsGroup(name, 3));
    groups.AddRange(RdsDecoderTestSeam.PsCycle(name));
    groups.AddRange(RdsDecoderTestSeam.PsCycle(name));

    RdsDecoderTestSeam.FeedSyntheticGroups(decoder, groups);

    Assert.NotEmpty(seen);
    Assert.All(seen, n => Assert.Equal("ROCK 92", n));
    Assert.Equal("ROCK 92", decoder.StationName);
  }

  // A byte outside printable ASCII used to fail the segment's validation, so
  // the slot was never written and kept whatever the previous page left
  // there. 0x82 is 'é' in the RDS basic character table (IEC 62106 E.1).
  [Fact]
  public void AccentedPsCharacter_DecodesThroughBasicCharset()
  {
    var decoder = new RdsDecoder(SampleRate);

    RdsDecoderTestSeam.FeedSyntheticRdsSignal(decoder, "CAF\u0082 FM ");

    Assert.Equal("CAFé FM", decoder.StationName);
  }

  #endregion

  #region Test Helpers

  /// <summary>
  /// Builds a valid 26-bit RDS word (16-bit data + 10-bit CRC with offset).
  /// </summary>
  private static uint BuildValidBlock(ushort data, int blockIndex)
  {
    // Start with the 16-bit data in the upper bits
    uint word26 = (uint)data << 10;

    // Compute CRC of the 16 data bits with 10 zero check bits
    var crc = ComputeRawCrc(data);

    // XOR with offset word to get the check bits
    var checkBits = (ushort)(crc ^ OffsetWords[blockIndex]);

    word26 |= checkBits;
    return word26;
  }

  /// <summary>
  /// Computes the raw 10-bit CRC for a 16-bit data word (before offset XOR).
  /// </summary>
  private static ushort ComputeRawCrc(ushort data)
  {
    uint reg = 0;
    // Process 16 data bits
    for (int i = 15; i >= 0; i--)
    {
      var bit = (data >> i) & 1;
      var feedback = (reg >> 9) & 1;
      reg = ((reg << 1) | (uint)bit) & 0x3FF;
      if (feedback == 1)
      {
        reg ^= CrcPoly;
      }
    }
    // Process 10 zero check bits
    for (int i = 0; i < 10; i++)
    {
      var feedback = (reg >> 9) & 1;
      reg = (reg << 1) & 0x3FF;
      if (feedback == 1)
      {
        reg ^= CrcPoly;
      }
    }
    return (ushort)(reg & 0x3FF);
  }

  #endregion
}
