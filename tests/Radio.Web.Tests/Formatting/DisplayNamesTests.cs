using System.Text.RegularExpressions;
using FluentAssertions;
using Radio.Web.Formatting;
using Radio.Web.Models;

namespace Radio.Web.Tests.Formatting;

/// <summary>
/// Tests <see cref="DisplayNames.Source(AudioSourceDto)"/>, <see cref="DisplayNames.Device(AudioDeviceDto, IDictionary{string, string}?)"/>,
/// and <see cref="DisplayNames.Track(NowPlayingDto)"/>.
/// </summary>
public class DisplayNamesTests
{
  // ---------- Source ----------

  [Fact]
  public void Source_NameMissing_GuidIdStripped_HumanizesType()
  {
    var s = new AudioSourceDto
    {
      Name = "",
      Id = "radio-aabbccdd-1122-3344-5566-778899aabbcc",
      Type = "Radio",
    };
    DisplayNames.Source(s).Should().Be("Radio");
  }

  [Fact]
  public void Source_NameSet_ReturnsName()
  {
    var s = new AudioSourceDto
    {
      Name = "My Radio",
      Id = "radio-aabbccdd-1122-3344-5566-778899aabbcc",
      Type = "Radio",
    };
    DisplayNames.Source(s).Should().Be("My Radio");
  }

  [Fact]
  public void Source_FilePlayerType_HumanizedToFilePlayerWithSpace()
  {
    var s = new AudioSourceDto { Name = "", Id = "", Type = "FilePlayer" };
    DisplayNames.Source(s).Should().Be("File Player");
  }

  [Fact]
  public void Source_NameAndTypeMissing_FallsBackToIdWithGuidStripped()
  {
    var s = new AudioSourceDto
    {
      Name = "",
      Id = "myhandle-aabbccdd-1122-3344-5566-778899aabbcc",
      Type = "",
    };
    DisplayNames.Source(s).Should().Be("myhandle");
  }

  [Fact]
  public void Source_ResultNeverContainsGuidCharacterSpan()
  {
    var s = new AudioSourceDto
    {
      Name = "",
      Id = "anything-aabbccdd-1122-3344-5566-778899aabbcc",
      Type = "",
    };
    var result = DisplayNames.Source(s);
    Regex.IsMatch(result, "[0-9a-f]{8}-[0-9a-f]{4}", RegexOptions.IgnoreCase)
      .Should().BeFalse("display names must never leak GUIDs to the UI");
  }

  // ---------- Device ----------

  [Fact]
  public void Device_KnownDriverSuffixInParens_HeadKept()
  {
    var d = new AudioDeviceDto { Name = "LG TV SSCR2 (AMD High Definition Audio Device)" };
    DisplayNames.Device(d, null).Should().Be("LG TV SSCR2");
  }

  [Fact]
  public void Device_AliasMapHit_ReturnsAlias()
  {
    var d = new AudioDeviceDto { Name = "CABLE Input (VB-Audio Virtual Cable)" };
    var aliases = new Dictionary<string, string>
    {
      ["CABLE Input (VB-Audio Virtual Cable)"] = "VB-Audio Cable In",
    };
    DisplayNames.Device(d, aliases).Should().Be("VB-Audio Cable In");
  }

  [Fact]
  public void Device_EnumerationPrefix_Stripped()
  {
    var d = new AudioDeviceDto { Name = "0 - LG TV" };
    DisplayNames.Device(d, null).Should().Be("LG TV");
  }

  [Fact]
  public void Device_NameOver40Chars_TruncatedTo39CharsPlusEllipsis()
  {
    // Construct a 50-character name with no parens and no trailing space at position 38
    // so the cap result is exactly 39 chars + the single-character ellipsis (U+2026) = 40.
    // Pattern is letters + digits, no whitespace near the cut, so TrimEnd is a no-op.
    var name = new string('A', 50);
    var d = new AudioDeviceDto { Name = name };
    var result = DisplayNames.Device(d, null);
    result.Length.Should().Be(40);
    result.EndsWith('…').Should().BeTrue();
    result.Should().Be(new string('A', 39) + "…");
  }

  [Fact]
  public void Device_60CharName_TruncatesTo39CharsPlusEllipsis()
  {
    // Task #15 PR B / handoff item #3 — a 60-char fixture is the canonical
    // long-name regression case (Bluetooth speakers + USB cards routinely
    // approach this length on Linux PipeWire descriptions). The cap result
    // is still 39 chars + the U+2026 ellipsis = 40, never the original 60.
    var name = new string('A', 60);
    var d = new AudioDeviceDto { Name = name };
    var result = DisplayNames.Device(d, null);
    result.Length.Should().Be(40);
    result.EndsWith('…').Should().BeTrue();
    result.Should().Be(new string('A', 39) + "…");
  }

  [Fact]
  public void Device_HeuristicHeadStrip_AppliedWhenHeadHasSpaceAndIs4Plus()
  {
    // "BigCo Speaker" (13 chars, has a space) head → stripped, even though the paren content
    // is not in the known-driver allow list.
    var d = new AudioDeviceDto { Name = "BigCo Speaker (Some Obscure Codec)" };
    DisplayNames.Device(d, null).Should().Be("BigCo Speaker");
  }

  [Fact]
  public void Device_HeuristicHeadStrip_SkippedWhenHeadHasNoSpace()
  {
    // Head "Speaker" (no space) and not a known suffix → keep the original.
    var d = new AudioDeviceDto { Name = "Speaker (Some Obscure Codec)" };
    DisplayNames.Device(d, null).Should().Be("Speaker (Some Obscure Codec)");
  }

  [Fact]
  public void Device_NameMissing_ReturnsEmDash()
  {
    var d = new AudioDeviceDto { Name = "" };
    DisplayNames.Device(d, null).Should().Be("—");
  }

  [Fact]
  public void Device_AliasMap_Wins_OverEnumerationPrefixStrip()
  {
    // Even when "0 - LG TV" would normally be cleaned to "LG TV", a whole-string alias hit wins.
    var d = new AudioDeviceDto { Name = "0 - LG TV" };
    var aliases = new Dictionary<string, string> { ["0 - LG TV"] = "Living Room TV" };
    DisplayNames.Device(d, aliases).Should().Be("Living Room TV");
  }

  // ---------- Track ----------

  [Fact]
  public void Track_GenericTitle_ParsesFromFilePath()
  {
    var np = new NowPlayingDto
    {
      Title = "Track 8",
      Artist = "Cary High Chorus",
      ExtendedMetadata = new Dictionary<string, object>
      {
        ["FilePath"] = @"C:\music\Cary High Chorus\2006 Fall Concert\08 opening night.mp3",
      },
    };
    var (title, subtitle) = DisplayNames.Track(np);
    title.Should().Be("Opening Night");
    subtitle.Should().Be("Cary High Chorus");
  }

  [Fact]
  public void Track_NonGenericTitle_Preserved()
  {
    var np = new NowPlayingDto { Title = "Sweet Child O' Mine", Artist = "Guns N' Roses" };
    var (title, _) = DisplayNames.Track(np);
    title.Should().Be("Sweet Child O' Mine");
  }

  [Fact]
  public void Track_EmptyArtist_ReturnsEmDash()
  {
    var np = new NowPlayingDto { Title = "Some Title", Artist = "" };
    var (_, subtitle) = DisplayNames.Track(np);
    subtitle.Should().Be("—");
  }

  [Fact]
  public void Track_DefaultPlaceholderArtist_ReturnsEmDash()
  {
    // NowPlayingDto initializes Artist to "--" by default — treat as missing.
    var np = new NowPlayingDto { Title = "Some Title" };
    var (_, subtitle) = DisplayNames.Track(np);
    subtitle.Should().Be("—");
  }

  [Fact]
  public void Track_GenericTitleAndNoFilePath_KeepsOriginalTitle()
  {
    var np = new NowPlayingDto { Title = "Track 12", Artist = "Various" };
    var (title, subtitle) = DisplayNames.Track(np);
    title.Should().Be("Track 12");
    subtitle.Should().Be("Various");
  }

  [Fact]
  public void Track_FilePathWithMixedCase_PreservesOriginalCasing()
  {
    var np = new NowPlayingDto
    {
      Title = "",
      ExtendedMetadata = new Dictionary<string, object>
      {
        ["FilePath"] = @"/music/03 - Bohemian Rhapsody.flac",
      },
    };
    var (title, _) = DisplayNames.Track(np);
    title.Should().Be("Bohemian Rhapsody");
  }

  [Fact]
  public void Track_GenericTitle_ReadsTypedFilePathProperty_Preferred()
  {
    // PR 3 added a first-class FilePath property to NowPlayingDto. When present, it
    // should be preferred over the (legacy) ExtendedMetadata["FilePath"] entry.
    var np = new NowPlayingDto
    {
      Title = "Track 5",
      Artist = "Some Artist",
      FilePath = @"C:\music\Some Artist\Album\05 typed property wins.mp3",
      ExtendedMetadata = new Dictionary<string, object>
      {
        ["FilePath"] = @"C:\music\should-be-ignored.mp3",
      },
    };
    var (title, subtitle) = DisplayNames.Track(np);
    title.Should().Be("Typed Property Wins");
    subtitle.Should().Be("Some Artist");
  }

  [Fact]
  public void Track_NullTypedFilePath_FallsBackToExtendedMetadata()
  {
    // Backward-compat: callers that didn't populate the typed FilePath but did stash
    // the path in ExtendedMetadata must still resolve a clean title.
    var np = new NowPlayingDto
    {
      Title = "Track 1",
      FilePath = null,
      ExtendedMetadata = new Dictionary<string, object>
      {
        ["FilePath"] = "/music/legacy fallback.mp3",
      },
    };
    var (title, _) = DisplayNames.Track(np);
    title.Should().Be("Legacy Fallback");
  }

  [Fact]
  public void Track_TypedFilePathWithEmptyString_TreatedAsNull()
  {
    // Whitespace-only typed values must not short-circuit the metadata fallback.
    var np = new NowPlayingDto
    {
      Title = "Track 7",
      FilePath = "   ",
      ExtendedMetadata = new Dictionary<string, object>
      {
        ["FilePath"] = "/music/whitespace recovers.flac",
      },
    };
    var (title, _) = DisplayNames.Track(np);
    title.Should().Be("Whitespace Recovers");
  }
}
