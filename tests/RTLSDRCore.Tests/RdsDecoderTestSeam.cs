using RTLSDRCore.DSP;

namespace RTLSDRCore.Tests;

/// <summary>
/// One RDS group for the synthetic signal generator: the four 16-bit data
/// words, plus an optional block to corrupt so that its syndrome check fails
/// while the other three blocks stay valid (models a burst error inside one
/// group without disturbing block alignment).
/// </summary>
internal readonly record struct RdsTestGroup(
  ushort BlockA, ushort BlockB, ushort BlockC, ushort BlockD, int? CorruptBlock = null);

/// <summary>
/// Shared synthetic-RDS-signal helpers used by both <see cref="RdsDecoderTests"/>
/// and <see cref="RadioReceiverTests"/>. Keeps the BPSK / biphase / CRC modulation
/// logic in one place so receiver-level integration tests can drive the same
/// path the decoder unit tests use.
/// </summary>
internal static class RdsDecoderTestSeam
{
  private const int SampleRate = 240000;
  private const float PilotFrequency = 19000f;
  private const float RdsCarrierFrequency = 57000f;
  private const float BaudRate = 1187.5f;
  private const float TwoPi = 2.0f * MathF.PI;

  // RDS CRC polynomial and offset words
  private const ushort CrcPoly = 0x5B9;
  private static readonly ushort[] OffsetWords = { 0x0FC, 0x198, 0x168, 0x1B4 };

  /// <summary>
  /// Complete PS cycles of one name that the generator sends for
  /// <see cref="FeedSyntheticRdsSignal"/>. The decoder needs two consecutive
  /// complete in-order cycles to confirm a name, and sync acquisition
  /// consumes part of the first cycle, so four would be the bare minimum;
  /// six leaves headroom for the clock-recovery settle.
  /// </summary>
  private const int PsRepetitions = 6;

  /// <summary>
  /// Generates a synthetic RDS-modulated FM composite signal carrying the
  /// given 8-character PS name and feeds it to the decoder. Drives the same
  /// path real-world RDS data takes through <c>RdsDecoder.Process</c>.
  /// Optionally accepts a PI code so tests targeting the PI-based call-sign
  /// decode (Task #80 v4) can verify a specific value rather than the
  /// default 0x1234.
  /// </summary>
  internal static void FeedSyntheticRdsSignal(RdsDecoder decoder, string psName, ushort piCode = 0x1234)
  {
    var groups = new List<RdsTestGroup>();
    for (int rep = 0; rep < PsRepetitions; rep++)
    {
      groups.AddRange(PsCycle(psName, piCode));
    }
    FeedSyntheticGroups(decoder, groups);
  }

  /// <summary>
  /// The four group 0A segments (addresses 0-3, in order) that carry one
  /// complete 8-character PS name. Characters are sent as their low byte, so
  /// a <c>'\u0082'</c> in the name goes out as RDS code 0x82.
  /// </summary>
  internal static IEnumerable<RdsTestGroup> PsCycle(string psName, ushort piCode = 0x1234)
  {
    if (psName.Length != 8)
    {
      throw new ArgumentException("PS name must be exactly 8 characters", nameof(psName));
    }

    for (int segment = 0; segment < 4; segment++)
    {
      yield return PsGroup(psName, segment, piCode);
    }
  }

  /// <summary>One group 0A carrying segment <paramref name="segment"/> of <paramref name="psName"/>.</summary>
  internal static RdsTestGroup PsGroup(string psName, int segment, ushort piCode = 0x1234, int? corruptBlock = null)
  {
    // Block B: group type 0A (0000 in bits 15-12, version A in bit 11),
    // TP=0, PTY=0, segment address in bits 1-0.
    var blockB = (ushort)(0x0000 | (segment & 0x03));
    var c1 = (byte)psName[segment * 2];
    var c2 = (byte)psName[segment * 2 + 1];
    var blockD = (ushort)((c1 << 8) | c2);
    return new RdsTestGroup(piCode, blockB, 0x0000 /* AF, arbitrary */, blockD, corruptBlock);
  }

  /// <summary>
  /// Modulates the given groups, in order, onto a 57 kHz BPSK subcarrier and
  /// feeds the composite to the decoder in SDR-sized blocks. A group whose
  /// <see cref="RdsTestGroup.CorruptBlock"/> is set has one data bit of that
  /// block flipped after the check word was computed, so the block fails the
  /// syndrome check; alignment of the surrounding blocks is unaffected.
  /// </summary>
  internal static void FeedSyntheticGroups(RdsDecoder decoder, IReadOnlyList<RdsTestGroup> groups)
  {
    var bits = new List<int>();
    foreach (var group in groups)
    {
      AppendBlock(bits, group.BlockA, 0, corrupt: group.CorruptBlock == 0);
      AppendBlock(bits, group.BlockB, 1, corrupt: group.CorruptBlock == 1);
      AppendBlock(bits, group.BlockC, 2, corrupt: group.CorruptBlock == 2);
      AppendBlock(bits, group.BlockD, 3, corrupt: group.CorruptBlock == 3);
    }

    // Differential encode
    var symbols = new int[bits.Count];
    int prevSymbol = 0;
    for (int i = 0; i < bits.Count; i++)
    {
      symbols[i] = bits[i] ^ prevSymbol;
      prevSymbol = symbols[i];
    }

    // Biphase (Manchester) encoding
    var chips = new List<int>();
    for (int i = 0; i < symbols.Length; i++)
    {
      if (symbols[i] == 1)
      {
        chips.Add(1);
        chips.Add(-1);
      }
      else
      {
        chips.Add(-1);
        chips.Add(1);
      }
    }

    // BPSK modulation on 57 kHz subcarrier
    var samplesPerChip = (float)SampleRate / (BaudRate * 2);
    var totalSamples = (int)(chips.Count * samplesPerChip) + SampleRate;
    var composite = new float[totalSamples];

    var settlingLength = SampleRate / 2;
    for (int i = 0; i < settlingLength; i++)
    {
      var t = (float)i / SampleRate;
      composite[i] = 0.05f * MathF.Cos(TwoPi * RdsCarrierFrequency * t);
    }

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

    // Feed in blocks
    const int blockSize = 4800;
    var pllPhase = 0f;
    for (int offset = 0; offset < totalSamples; offset += blockSize)
    {
      var count = Math.Min(blockSize, totalSamples - offset);
      decoder.Process(composite.AsSpan(offset, count), count, pllPhase, PilotFrequency);

      pllPhase += TwoPi * PilotFrequency * count / SampleRate;
      pllPhase %= TwoPi;
    }
  }

  private static uint BuildValidBlock(ushort data, int blockIndex)
  {
    uint word26 = (uint)data << 10;
    var crc = ComputeRawCrc(data);
    var checkBits = (ushort)(crc ^ OffsetWords[blockIndex]);
    word26 |= checkBits;
    return word26;
  }

  private static ushort ComputeRawCrc(ushort data)
  {
    uint reg = 0;
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

  private static void AppendBlock(List<int> bits, ushort data, int blockIndex, bool corrupt = false)
  {
    var word26 = BuildValidBlock(data, blockIndex);
    if (corrupt)
    {
      // Flip one data bit after the check word was computed. The (26,16)
      // code detects every single-bit error, so the syndrome check fails.
      word26 ^= 1u << 15;
    }
    for (int i = 25; i >= 0; i--)
    {
      bits.Add((int)((word26 >> i) & 1));
    }
  }
}
