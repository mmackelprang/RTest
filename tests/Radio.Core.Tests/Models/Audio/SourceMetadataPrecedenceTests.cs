using Radio.Core.Models.Audio;
using Xunit;

namespace Radio.Core.Tests.Models.Audio;

/// <summary>
/// AUD-1. The owner's rule: source metadata wins where it exists; fingerprinting fills only
/// what is missing, decided per field. These tests are the definition of "missing".
/// </summary>
public class SourceMetadataPrecedenceTests
{
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("\t")]
  public void IsMissing_NullEmptyOrWhitespace_IsMissing(string? value)
  {
    // The empty string is the owner's named edge, and on Bluetooth it is the COMMON case:
    // BluetoothPlaybackMetadata.Album defaults to string.Empty and is written through
    // unguarded, so "" is what "the phone reported no album" looks like in the dictionary.
    Assert.True(SourceMetadataPrecedence.IsMissing(value));
  }

  [Theory]
  [InlineData("--", StandardMetadataKeys.DefaultArtist)]
  [InlineData(" -- ", StandardMetadataKeys.DefaultArtist)]
  [InlineData(StandardMetadataKeys.DefaultAlbumArtUrl, StandardMetadataKeys.DefaultAlbumArtUrl)]
  [InlineData("08-TRACK", "08-track")]
  public void IsMissing_PlaceholderSentinel_IsMissing(string value, string placeholder)
  {
    Assert.True(SourceMetadataPrecedence.IsMissing(value, placeholder));
  }

  [Fact]
  public void IsMissing_RealValue_IsNotMissing()
  {
    Assert.False(SourceMetadataPrecedence.IsMissing("Metallica", StandardMetadataKeys.DefaultArtist));
  }

  [Fact]
  public void IsMissing_ValueThatMerelyContainsThePlaceholder_IsNotMissing()
  {
    // Equality, not Contains — "--" inside a real title must not read as missing.
    Assert.False(SourceMetadataPrecedence.IsMissing("Blink-182 -- Greatest Hits", "--"));
  }

  [Fact]
  public void IsMissing_EmptyPlaceholder_DoesNotMakeARealValueMissing()
  {
    // A caller passing an empty placeholder (e.g. no current file) must not match anything real.
    Assert.False(SourceMetadataPrecedence.IsMissing("Enter Sandman", "", "   "));
  }

  [Fact]
  public void IsFieldMissing_AbsentKey_IsMissing()
  {
    var metadata = new Dictionary<string, object>();
    Assert.True(SourceMetadataPrecedence.IsFieldMissing(metadata, StandardMetadataKeys.AlbumArtUrl));
  }

  [Fact]
  public void TryFill_DoesNotOverwriteAValueTheSourceSupplied()
  {
    var metadata = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "Enter Sandman (Remastered)"
    };

    var wrote = SourceMetadataPrecedence.TryFill(metadata, StandardMetadataKeys.Title, "Enter Sandman");

    Assert.False(wrote);
    Assert.Equal("Enter Sandman (Remastered)", metadata[StandardMetadataKeys.Title]);
  }

  [Fact]
  public void TryFill_FillsAnEmptyStringAndAnAbsentKey()
  {
    var metadata = new Dictionary<string, object> { [StandardMetadataKeys.Album] = "" };

    Assert.True(SourceMetadataPrecedence.TryFill(
      metadata, StandardMetadataKeys.Album, "Metallica", StandardMetadataKeys.DefaultAlbum));
    Assert.True(SourceMetadataPrecedence.TryFill(metadata, StandardMetadataKeys.Artist, "Metallica"));

    Assert.Equal("Metallica", metadata[StandardMetadataKeys.Album]);
    Assert.Equal("Metallica", metadata[StandardMetadataKeys.Artist]);
  }

  [Fact]
  public void TryFill_IgnoresAnEmptyCandidate()
  {
    // A fingerprint result with no album must not write "" over "" and claim it filled.
    var metadata = new Dictionary<string, object> { [StandardMetadataKeys.Album] = StandardMetadataKeys.DefaultAlbum };

    Assert.False(SourceMetadataPrecedence.TryFill(metadata, StandardMetadataKeys.Album, ""));
    Assert.False(SourceMetadataPrecedence.TryFill(metadata, StandardMetadataKeys.Album, null));
    Assert.Equal(StandardMetadataKeys.DefaultAlbum, metadata[StandardMetadataKeys.Album]);
  }

  [Fact]
  public void FillMissingFrom_DecidesEachFieldIndependently()
  {
    // The core of the rule: title supplied, artist supplied, album empty. Only the album moves.
    var metadata = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "Enter Sandman (Remastered)",
      [StandardMetadataKeys.Artist] = "Metallica",
      [StandardMetadataKeys.Album] = ""
    };

    var filled = SourceMetadataPrecedence.FillMissingFrom(metadata, Track("Enter Sandman", "Metallica", "Metallica"));

    Assert.Equal(new MetadataFillResult(Title: false, Artist: false, Album: true), filled);
    Assert.Equal("Enter Sandman (Remastered)", metadata[StandardMetadataKeys.Title]);
    Assert.Equal("Metallica", metadata[StandardMetadataKeys.Album]);
    Assert.Equal("album", filled.Describe());
  }

  [Fact]
  public void FillMissingFrom_TreatsExtraTitlePlaceholdersAsNoTitle()
  {
    // FilePlayer seeds Title with the filename; Bluetooth writes the device name as the
    // title when it has no track metadata. Both mean "no title".
    var metadata = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "Pixel 10 Pro XL",
      [StandardMetadataKeys.Artist] = StandardMetadataKeys.DefaultArtist,
      [StandardMetadataKeys.Album] = StandardMetadataKeys.DefaultAlbum
    };

    var filled = SourceMetadataPrecedence.FillMissingFrom(
      metadata, Track("Heart and Soul", "Huey Lewis & The News", "Sports"), "Bluetooth", null, "Pixel 10 Pro XL");

    Assert.True(filled.Any);
    Assert.Equal("Heart and Soul", metadata[StandardMetadataKeys.Title]);
    Assert.Equal("title, artist, album, cover art", filled.Describe(filledAlbumArt: true));
  }

  [Fact]
  public void FillMissingFrom_TheDefaultTitleIsAlwaysAPlaceholder()
  {
    var metadata = new Dictionary<string, object> { [StandardMetadataKeys.Title] = StandardMetadataKeys.DefaultTitle };

    var filled = SourceMetadataPrecedence.FillMissingFrom(metadata, Track("Heart and Soul", "Huey Lewis & The News"));

    Assert.True(filled.Title);
    Assert.Equal("Heart and Soul", metadata[StandardMetadataKeys.Title]);
  }

  [Fact]
  public void FillMissingFrom_WithEverythingSupplied_WritesNothing()
  {
    var metadata = new Dictionary<string, object>
    {
      [StandardMetadataKeys.Title] = "All the Small Things",
      [StandardMetadataKeys.Artist] = "Blink-182",
      [StandardMetadataKeys.Album] = "Enema of the State"
    };

    // U+2010 HYPHEN — the corruption measured on the appliance.
    var filled = SourceMetadataPrecedence.FillMissingFrom(metadata, Track("All the Small Things", "blink‐182", "Wrong"));

    Assert.False(filled.Any);
    Assert.Equal("nothing", filled.Describe());
    Assert.Equal("Blink-182", metadata[StandardMetadataKeys.Artist]);
    Assert.Equal("Enema of the State", metadata[StandardMetadataKeys.Album]);
  }

  [Fact]
  public void ShouldFillAlbumArt_AbsentEmptyOrFallback_True_RealPath_False()
  {
    Assert.True(SourceMetadataPrecedence.ShouldFillAlbumArt(new Dictionary<string, object>()));
    Assert.True(SourceMetadataPrecedence.ShouldFillAlbumArt(new Dictionary<string, object>
    {
      [StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl
    }));
    Assert.True(SourceMetadataPrecedence.ShouldFillAlbumArt(new Dictionary<string, object>
    {
      [StandardMetadataKeys.AlbumArtUrl] = ""
    }));
    Assert.False(SourceMetadataPrecedence.ShouldFillAlbumArt(new Dictionary<string, object>
    {
      [StandardMetadataKeys.AlbumArtUrl] = "/api/albumart/c8d4539b92c87615.jpg"
    }));
  }

  private static TrackMetadata Track(string title, string artist, string? album = null) => new()
  {
    Id = Guid.NewGuid().ToString(),
    Title = title,
    Artist = artist,
    Album = album,
    Source = MetadataSource.Shazam,
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
  };
}
