using System.Text;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Encodes raw PCM audio data into a self-contained WAV chunk with a standard
/// 44-byte RIFF/WAVE header. Each chunk is independently decodable, which is
/// essential for the Direct Cast Channel streaming mode where chunks are sent
/// as individual messages and decoded with <c>AudioContext.decodeAudioData()</c>.
/// </summary>
/// <remarks>
/// WAV format reference: http://soundfile.sapp.org/doc/WaveFormat/
/// The header uses PCM format (audioFormat = 1) with no extra parameters.
/// </remarks>
public static class WavChunkEncoder
{
  private const int HeaderSize = 44;
  private const short AudioFormatPcm = 1;

  /// <summary>
  /// Wraps raw PCM data in a WAV container with a standard 44-byte header.
  /// </summary>
  /// <param name="pcmData">Buffer containing raw PCM audio samples (little-endian, interleaved).</param>
  /// <param name="pcmLength">Number of valid bytes in <paramref name="pcmData"/> to encode.</param>
  /// <param name="sampleRate">Sample rate in Hz (e.g., 48000).</param>
  /// <param name="channels">Number of audio channels (1 = mono, 2 = stereo).</param>
  /// <param name="bitsPerSample">Bits per sample (typically 16).</param>
  /// <returns>A byte array containing the complete WAV file (header + PCM data).</returns>
  public static byte[] Encode(byte[] pcmData, int pcmLength, int sampleRate, int channels, int bitsPerSample = 16)
  {
    var blockAlign = (short)(channels * bitsPerSample / 8);
    var byteRate = sampleRate * blockAlign;
    var dataSize = pcmLength;
    var fileSize = HeaderSize + dataSize - 8; // RIFF chunk size excludes first 8 bytes

    var wav = new byte[HeaderSize + dataSize];

    // RIFF header
    Encoding.ASCII.GetBytes("RIFF", 0, 4, wav, 0);
    BitConverter.GetBytes(fileSize).CopyTo(wav, 4);
    Encoding.ASCII.GetBytes("WAVE", 0, 4, wav, 8);

    // fmt sub-chunk
    Encoding.ASCII.GetBytes("fmt ", 0, 4, wav, 12);
    BitConverter.GetBytes(16).CopyTo(wav, 16);               // Sub-chunk size (16 for PCM)
    BitConverter.GetBytes(AudioFormatPcm).CopyTo(wav, 20);    // Audio format
    BitConverter.GetBytes((short)channels).CopyTo(wav, 22);   // Channels
    BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);        // Sample rate
    BitConverter.GetBytes(byteRate).CopyTo(wav, 28);          // Byte rate
    BitConverter.GetBytes(blockAlign).CopyTo(wav, 32);        // Block align
    BitConverter.GetBytes((short)bitsPerSample).CopyTo(wav, 34); // Bits per sample

    // data sub-chunk
    Encoding.ASCII.GetBytes("data", 0, 4, wav, 36);
    BitConverter.GetBytes(dataSize).CopyTo(wav, 40);

    // PCM data
    Buffer.BlockCopy(pcmData, 0, wav, HeaderSize, dataSize);

    return wav;
  }
}
