using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth.Linux;

/// <summary>
/// Pure parser for BlueZ <c>MediaTransport1.Codec</c> + <c>Configuration</c>
/// properties. No I/O — fully testable.
///
/// BlueZ codec IDs (from kernel <c>net/bluetooth/a2dp-codecs.h</c>):
///   <list type="bullet">
///     <item>0x00 = SBC</item>
///     <item>0x02 = MPEG-2/4 AAC</item>
///     <item>0xFF = vendor-specific (aptX, aptX-HD, LDAC, LHDC, ...)</item>
///   </list>
///
/// SBC Configuration layout (4 bytes):
///   <list type="bullet">
///     <item>byte 0: sampling-freq (bits 4..7) | channel-mode (bits 0..3)</item>
///     <item>byte 1: block-length (bits 4..7) | subbands (bits 2..3) | allocation (bits 0..1)</item>
///     <item>byte 2: min-bitpool</item>
///     <item>byte 3: max-bitpool</item>
///   </list>
///
/// AAC Configuration is 6 bytes (object-type / sample-freq-12bit / channels /
/// VBR+bitrate). Vendor-specific Configuration starts with a 6-byte header
/// (4-byte little-endian vendor ID + 2-byte little-endian codec ID).
/// </summary>
internal static class A2dpCodecConfigParser
{
  public static A2dpCodecInfo Parse(byte codecId, byte[] configuration)
  {
    var name = CodecName(codecId, configuration);
    var sampleRate = SampleRate(codecId, configuration);
    var bitpool = (codecId == 0x00 && configuration.Length >= 4)
      ? configuration[3]  // max-bitpool
      : (int?)null;

    return new A2dpCodecInfo
    {
      CodecId = codecId,
      CodecName = name,
      SampleRateHz = sampleRate,
      BitpoolOrNull = bitpool,
      RawConfiguration = configuration
    };
  }

  internal static string CodecName(byte codecId, byte[] configuration) => codecId switch
  {
    0x00 => "SBC",
    0x02 => "AAC",
    0xFF when configuration.Length >= 6 => ParseVendorCodec(configuration),
    _ => $"Unknown-0x{codecId:X2}"
  };

  /// <summary>
  /// For vendor-specific (0xFF) codec, the first 6 bytes of Configuration are:
  ///   <list type="bullet">
  ///     <item>bytes 0..3: vendor ID (little-endian, Bluetooth SIG company ID)</item>
  ///     <item>bytes 4..5: codec ID (little-endian, vendor-defined)</item>
  ///   </list>
  /// </summary>
  internal static string ParseVendorCodec(byte[] configuration)
  {
    var vendorId = (uint)(configuration[0] | (configuration[1] << 8) | (configuration[2] << 16) | (configuration[3] << 24));
    var codecId = (ushort)(configuration[4] | (configuration[5] << 8));
    // Vendor IDs from Bluetooth SIG company ID assignments.
    return (vendorId, codecId) switch
    {
      (0x004F, 0x0001) => "aptX",
      (0x000A, 0x0001) => "aptX",         // Qualcomm/CSR
      (0x00D7, 0x0024) => "aptX-HD",      // Qualcomm extension
      (0x012D, 0x00AA) => "LDAC",         // Sony
      (0x053A, 0x4C32) => "LHDC",         // Savitech
      _ => $"Vendor-0x{vendorId:X8}/0x{codecId:X4}"
    };
  }

  internal static int SampleRate(byte codecId, byte[] configuration)
  {
    if (codecId == 0x00 && configuration.Length >= 1)
    {
      // SBC: sampling-freq in bits 4..7 of byte 0.
      var freqBits = (configuration[0] >> 4) & 0x0F;
      return freqBits switch
      {
        0x08 => 16000,
        0x04 => 32000,
        0x02 => 44100,
        0x01 => 48000,
        _ => 0
      };
    }
    if (codecId == 0x02 && configuration.Length >= 4)
    {
      // AAC: sampling-freq is a 12-bit field. Per a2dp-codecs.h:
      //   byte 1 = all 8 high bits of the 12-bit field
      //   byte 2 high nibble = the low 4 bits of the 12-bit field
      var freqBits = ((configuration[1] & 0xFF) << 4) | ((configuration[2] >> 4) & 0x0F);
      return freqBits switch
      {
        0x800 => 8000,
        0x400 => 11025,
        0x200 => 12000,
        0x100 => 16000,
        0x080 => 22050,
        0x040 => 24000,
        0x020 => 32000,
        0x010 => 44100,
        0x008 => 48000,
        0x004 => 64000,
        0x002 => 88200,
        0x001 => 96000,
        _ => 0
      };
    }
    return 0;
  }
}
