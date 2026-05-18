using RTLSDRCore.DSP;

namespace RTLSDRCore.Tests;

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
  /// Generates a synthetic RDS-modulated FM composite signal carrying the
  /// given 8-character PS name and feeds it to the decoder. Drives the same
  /// path real-world RDS data takes through <c>RdsDecoder.Process</c>.
  /// </summary>
  internal static void FeedSyntheticRdsSignal(RdsDecoder decoder, string psName)
  {
    if (psName.Length != 8)
    {
      throw new ArgumentException("PS name must be exactly 8 characters", nameof(psName));
    }

    var bits = new List<int>();
    var piCode = (ushort)0x1234; // arbitrary PI code

    // Repeat 4 times for noise rejection (PsConfirmThreshold = 2, need
    // multiple complete names to confirm).
    for (int rep = 0; rep < 4; rep++)
    {
      for (int charPair = 0; charPair < 4; charPair++)
      {
        AppendBlock(bits, piCode, 0);

        // Group type 0A, char pair index in bits 1-0
        ushort blockB = (ushort)(0x0000 | (charPair & 0x03));
        AppendBlock(bits, blockB, 1);

        AppendBlock(bits, 0x0000, 2); // AF data (arbitrary)

        var c1 = (byte)psName[charPair * 2];
        var c2 = (byte)psName[charPair * 2 + 1];
        ushort blockD = (ushort)((c1 << 8) | c2);
        AppendBlock(bits, blockD, 3);
      }
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

  private static void AppendBlock(List<int> bits, ushort data, int blockIndex)
  {
    var word26 = BuildValidBlock(data, blockIndex);
    for (int i = 25; i >= 0; i--)
    {
      bits.Add((int)((word26 >> i) & 1));
    }
  }
}
