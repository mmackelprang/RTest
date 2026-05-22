using Radio.Infrastructure.Platform.Bluetooth.Linux;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth.Linux;

public class A2dpCodecConfigParserTests
{
  [Theory]
  [InlineData(0x00, "SBC")]
  [InlineData(0x02, "AAC")]
  [InlineData(0x05, "Unknown-0x05")]
  public void CodecName_KnownAndUnknown(byte codecId, string expected)
  {
    Assert.Equal(expected, A2dpCodecConfigParser.CodecName(codecId, new byte[6]));
  }

  [Fact]
  public void ParseSBC_48kHz_StereoJoint_Bitpool53()
  {
    // SBC sampling-freq is a 4-bit flag in the high nibble of byte 0 per
    // a2dp-codecs.h: 0x1 = 48 kHz, 0x2 = 44.1 kHz, 0x4 = 32 kHz, 0x8 = 16 kHz.
    // byte 0 = 0x11 → high nibble 0x1 (48 kHz) | low nibble 0x1 (joint-stereo).
    // byte 3 = 53 (max bitpool).
    byte[] config = { 0x11, 0x35, 2, 53 };
    var info = A2dpCodecConfigParser.Parse(0x00, config);
    Assert.Equal("SBC", info.CodecName);
    Assert.Equal(48000, info.SampleRateHz);
    Assert.Equal(53, info.BitpoolOrNull);
  }

  [Fact]
  public void ParseSBC_44kHz_Bitpool35()
  {
    // Parser exercised with { 0x22, 0x35, 2, 35 } — byte 0 high nibble = 0x2
    // → 44.1 kHz per the SBC sampling-freq bit-flag table in a2dp-codecs.h.
    var info = A2dpCodecConfigParser.Parse(0x00, new byte[] { 0x22, 0x35, 2, 35 });
    Assert.Equal(44100, info.SampleRateHz);
    Assert.Equal(35, info.BitpoolOrNull);
  }

  [Fact]
  public void ParseAAC_NoBitpool()
  {
    byte[] config = { 0x80, 0x01, 0x8C, 0x80, 0x00, 0xFA };  // arbitrary
    var info = A2dpCodecConfigParser.Parse(0x02, config);
    Assert.Equal("AAC", info.CodecName);
    Assert.Null(info.BitpoolOrNull);
  }

  [Fact]
  public void ParseVendor_aptX()
  {
    // aptX vendor=0x004F, codec=0x0001
    byte[] config = { 0x4F, 0x00, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00 };
    var info = A2dpCodecConfigParser.Parse(0xFF, config);
    Assert.Equal("aptX", info.CodecName);
  }

  [Fact]
  public void ParseVendor_LDAC()
  {
    // LDAC vendor=0x012D, codec=0x00AA
    byte[] config = { 0x2D, 0x01, 0x00, 0x00, 0xAA, 0x00, 0x20, 0x00 };
    var info = A2dpCodecConfigParser.Parse(0xFF, config);
    Assert.Equal("LDAC", info.CodecName);
  }

  [Fact]
  public void ParseVendor_Unknown_ReturnsHexId()
  {
    byte[] config = { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x00 };
    var info = A2dpCodecConfigParser.Parse(0xFF, config);
    Assert.StartsWith("Vendor-0x", info.CodecName);
  }

  [Fact]
  public void RawConfigurationPreserved()
  {
    byte[] config = { 0x21, 0x35, 2, 53 };
    var info = A2dpCodecConfigParser.Parse(0x00, config);
    Assert.Equal(config, info.RawConfiguration);
  }
}
