using Radio.Core.Utilities;

namespace Radio.Core.Tests.Utilities;

/// <summary>
/// Tests for the persisted output-device identity (AUD-6).
///
/// <para>
/// The behaviour worth guarding is not the string formatting — it is that a pre-AUD-6 ordinal is
/// recognised and refused rather than resolved. Resolving it would map a saved preference onto
/// whatever enumeration order exists today, and the whole defect is that that order is not
/// trustworthy.
/// </para>
/// </summary>
public class StableAudioDeviceKeyTests
{
  [Fact]
  public void ForOutput_RoundTripsTheRawName()
  {
    string key = StableAudioDeviceKey.ForOutput("Built-in Audio Analog Stereo");

    Assert.Equal("Built-in Audio Analog Stereo", StableAudioDeviceKey.RawNameFrom(key));
  }

  [Theory]
  [InlineData("SN6140 Analog")]
  [InlineData("HDMI 1")]
  [InlineData("CABLE Input (VB-Audio Virtual Cable)")]
  [InlineData("weird: name with colons: and spaces")]
  public void ForOutput_SurvivesNamesWithPunctuation(string rawName)
  {
    // The prefix is stripped by length, not by splitting on ':', so a name containing the
    // delimiter round-trips intact. Splitting would silently truncate real device names.
    Assert.Equal(rawName, StableAudioDeviceKey.RawNameFrom(StableAudioDeviceKey.ForOutput(rawName)));
  }

  [Theory]
  [InlineData("playback-0")]
  [InlineData("playback-1")]
  [InlineData("playback-12")]
  public void IsLegacyOrdinal_RecognisesPreAud6Ids(string legacy)
  {
    Assert.True(StableAudioDeviceKey.IsLegacyOrdinal(legacy));

    // And must not be mistaken for a name key, which would make "0" a device name.
    Assert.Null(StableAudioDeviceKey.RawNameFrom(legacy));
  }

  [Fact]
  public void ForOutput_ProducesSomethingNoLongerMistakableForAnOrdinal()
  {
    // The resolver branches on these being distinguishable. A device literally named "playback-1"
    // still produces "out:playback-1", which is a name key and not an ordinal.
    string key = StableAudioDeviceKey.ForOutput("playback-1");

    Assert.False(StableAudioDeviceKey.IsLegacyOrdinal(key));
    Assert.Equal("playback-1", StableAudioDeviceKey.RawNameFrom(key));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("google-cast")]
  [InlineData("http-stream")]
  public void RawNameFrom_ReturnsNullForAnythingThatIsNotAnOutputKey(string? id)
  {
    // The virtual outputs keep their own ids and must not be parsed as name keys.
    Assert.Null(StableAudioDeviceKey.RawNameFrom(id));
    Assert.False(StableAudioDeviceKey.IsLegacyOrdinal(id));
  }

  [Fact]
  public void ForOutput_Throws_OnNull()
  {
    Assert.Throws<ArgumentNullException>(() => StableAudioDeviceKey.ForOutput(null!));
  }
}
