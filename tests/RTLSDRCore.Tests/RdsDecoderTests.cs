using RTLSDRCore.DSP;
using Xunit;

namespace RTLSDRCore.Tests;

public class RdsDecoderTests
{
  private const int SampleRate = 240000;
  private const float PilotFrequency = 19000f;
  private const float RdsCarrierFrequency = 57000f;
  private const float BaudRate = 1187.5f;
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
    FeedSyntheticRdsSignal(decoder, "TEST FM ");

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
    FeedSyntheticRdsSignal(decoder, "KEXP-FM ");

    Assert.NotNull(decoder.StationName);
    Assert.Equal("KEXP-FM", decoder.StationName);
    Assert.True(decoder.RdsDetected);
  }

  [Fact]
  public void SyntheticRdsSignal_DifferentStationName()
  {
    var decoder = new RdsDecoder(SampleRate);
    FeedSyntheticRdsSignal(decoder, "KUOW    ");

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

    FeedSyntheticRdsSignal(decoder, "KEXP-FM ");

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

    FeedSyntheticRdsSignal(decoder, "WUNC-FM ");

    // The synthetic feed sends the PS name multiple times. The event
    // should fire more than once — that's the whole point: per-frame
    // sampling, not per-confirmation.
    Assert.True(fireCount >= 2, $"Expected at least 2 frame-level events, got {fireCount}");
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

  /// <summary>
  /// Generates synthetic RDS-modulated composite FM signal and feeds it to the decoder.
  /// Encodes a PS station name into Group 0A RDS blocks, modulates at 57 kHz BPSK.
  /// </summary>
  private static void FeedSyntheticRdsSignal(RdsDecoder decoder, string psName)
  {
    if (psName.Length != 8)
    {
      throw new ArgumentException("PS name must be exactly 8 characters", nameof(psName));
    }

    // Build the RDS bitstream: multiple repetitions of the complete PS name
    // via Group 0A blocks (4 groups per complete PS name, 4 blocks per group)
    var bits = new List<int>();
    var piCode = (ushort)0x1234; // arbitrary PI code

    // Repeat 4 times for noise rejection (PsConfirmThreshold = 2, need multiple complete names)
    for (int rep = 0; rep < 4; rep++)
    {
      for (int charPair = 0; charPair < 4; charPair++)
      {
        // Block A: PI code
        AppendBlock(bits, piCode, 0);

        // Block B: Group type 0A (0000 in bits 15-12), version A (0 in bit 11),
        // TP=0, PTY=0, char pair index in bits 1-0
        ushort blockB = (ushort)(0x0000 | (charPair & 0x03));
        AppendBlock(bits, blockB, 1);

        // Block C: AF data (arbitrary for Group 0A)
        AppendBlock(bits, 0x0000, 2);

        // Block D: Two PS characters
        var c1 = (byte)psName[charPair * 2];
        var c2 = (byte)psName[charPair * 2 + 1];
        ushort blockD = (ushort)((c1 << 8) | c2);
        AppendBlock(bits, blockD, 3);
      }
    }

    // Convert bits to differentially encoded symbols
    var symbols = new int[bits.Count];
    int prevSymbol = 0;
    for (int i = 0; i < bits.Count; i++)
    {
      // Differential encoding: symbol = bit XOR prevSymbol
      symbols[i] = bits[i] ^ prevSymbol;
      prevSymbol = symbols[i];
    }

    // Biphase (Manchester) encoding: each diff-encoded symbol → 2 chips of opposite polarity.
    // This matches the RDS standard where each bit period contains two half-periods.
    var chips = new List<int>();
    for (int i = 0; i < symbols.Length; i++)
    {
      if (symbols[i] == 1)
      {
        chips.Add(1);   // first half positive
        chips.Add(-1);  // second half negative
      }
      else
      {
        chips.Add(-1);  // first half negative
        chips.Add(1);   // second half positive
      }
    }

    // Modulate: BPSK on 57 kHz carrier at the given sample rate, one chip per half-symbol
    var samplesPerChip = (float)SampleRate / (BaudRate * 2); // ~101 samples/chip
    var totalSamples = (int)(chips.Count * samplesPerChip) + SampleRate; // extra 1s for filter settling
    var composite = new float[totalSamples];

    // Pre-fill with carrier (no data) for filter settling
    var settlingLength = SampleRate / 2; // 0.5s settling
    for (int i = 0; i < settlingLength; i++)
    {
      var t = (float)i / SampleRate;
      composite[i] = 0.05f * MathF.Cos(TwoPi * RdsCarrierFrequency * t);
    }

    // Modulate biphase chips
    for (int chip = 0; chip < chips.Count; chip++)
    {
      var amplitude = chips[chip] == 1 ? 0.05f : -0.05f;
      var startSample = settlingLength + (int)(chip * samplesPerChip);
      var endSample = settlingLength + (int)((chip + 1) * samplesPerChip);
      endSample = Math.Min(endSample, totalSamples);

      for (int i = startSample; i < endSample; i++)
      {
        var t = (float)i / SampleRate;
        composite[i] = amplitude * MathF.Cos(TwoPi * RdsCarrierFrequency * t);
      }
    }

    // Feed to decoder in blocks
    const int blockSize = 4800;
    var pllPhase = 0f;
    for (int offset = 0; offset < totalSamples; offset += blockSize)
    {
      var count = Math.Min(blockSize, totalSamples - offset);
      decoder.Process(composite.AsSpan(offset, count), count, pllPhase, PilotFrequency);

      // Advance PLL phase as if locked to pilot
      pllPhase += TwoPi * PilotFrequency * count / SampleRate;
      pllPhase %= TwoPi;
    }
  }

  /// <summary>
  /// Appends a 26-bit RDS block (16 data + 10 check) as individual bits to the list.
  /// </summary>
  private static void AppendBlock(List<int> bits, ushort data, int blockIndex)
  {
    var word26 = BuildValidBlock(data, blockIndex);

    // Append MSB first
    for (int i = 25; i >= 0; i--)
    {
      bits.Add((int)((word26 >> i) & 1));
    }
  }

  #endregion
}
