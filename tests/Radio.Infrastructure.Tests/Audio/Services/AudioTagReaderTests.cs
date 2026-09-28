using Radio.Infrastructure.Audio.Services;
using SoundFlow.Metadata;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-32: <see cref="AudioTagReader"/> must return a file's tags even when SoundFlow's
/// <see cref="SoundMetadataReader"/> rejects the file.
/// </summary>
/// <remarks>
/// The fixture is the first 18 KB of a real library file whose ID3v2.2 tag SoundFlow 1.4.1 reports
/// as <c>CorruptFrameError</c>, while TagLib reads title, artist and album from it. On the appliance
/// that failure left the file player with placeholder metadata, which fingerprinting then filled —
/// replacing a tagged artist with a wrong one.
/// </remarks>
public class AudioTagReaderTests : IDisposable
{
  private static readonly string Id3v22Fixture =
    Path.Combine(AppContext.BaseDirectory, "TestData", "id3v22-soundflow-rejects.mp3");

  private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"aud32-{Guid.NewGuid()}");

  public AudioTagReaderTests()
  {
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
    {
      Directory.Delete(_tempDir, true);
    }
  }

  [Fact]
  public void Fixture_IsStillRejectedBySoundFlow()
  {
    // Guards the fixture itself: if a SoundFlow upgrade starts accepting this tag, the fallback
    // tests below stop exercising the fallback and would pass vacuously.
    var result = SoundMetadataReader.Read(Id3v22Fixture);

    Assert.False(result.IsSuccess);
  }

  [Fact]
  public void Read_TagSoundFlowRejects_ReturnsTheTagsFromTagLib()
  {
    AudioFileTags? tags = AudioTagReader.Read(Id3v22Fixture);

    Assert.NotNull(tags);
    Assert.Equal("Meditating Beat", tags.Title);
    Assert.Equal("Kevin MacLeod", tags.Artist);
    Assert.Equal("FreePD Music", tags.Album);
    Assert.Equal(AudioTagReader.TagLibReader, tags.ReadBy);
  }

  [Fact]
  public void Read_TagSoundFlowRejects_StillReportsADuration()
  {
    AudioFileTags? tags = AudioTagReader.Read(Id3v22Fixture);

    Assert.NotNull(tags);
    Assert.NotNull(tags.Duration);
    Assert.True(tags.Duration > TimeSpan.Zero);
  }

  [Fact]
  public void Read_NotAudioAtAll_ReturnsNull()
  {
    var path = Path.Combine(_tempDir, "not-audio.mp3");
    File.WriteAllText(path, "this is not an mp3");

    Assert.Null(AudioTagReader.Read(path));
  }

  [Fact]
  public void Read_WhitespaceTags_AreReportedAsMissing()
  {
    var path = Path.Combine(_tempDir, "whitespace.mp3");
    File.Copy(Id3v22Fixture, path);
    using (var file = TagLib.File.Create(path))
    {
      file.Tag.Album = "   ";
      file.Save();
    }

    AudioFileTags? tags = AudioTagReader.Read(path);

    Assert.NotNull(tags);
    Assert.Null(tags.Album);
    Assert.Equal("Kevin MacLeod", tags.Artist);
  }
}
