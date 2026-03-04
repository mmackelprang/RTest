using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

public class SongRecRecognitionServiceTests
{
  private readonly Mock<ILogger<SongRecRecognitionService>> _loggerMock;

  public SongRecRecognitionServiceTests()
  {
    _loggerMock = new Mock<ILogger<SongRecRecognitionService>>();
  }

  [Fact]
  public void Constructor_WhenDisabled_SetsIsAvailableFalse()
  {
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = false }
    });

    var service = new SongRecRecognitionService(_loggerMock.Object, options);

    Assert.False(service.IsAvailable);
  }

  [Fact]
  public async Task RecognizeAsync_WhenNotAvailable_ReturnsNull()
  {
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = false }
    });
    var service = new SongRecRecognitionService(_loggerMock.Object, options);

    var samples = CreateTestSamples(5.0);
    var result = await service.RecognizeAsync(samples);

    Assert.Null(result);
  }

  [Fact]
  public async Task RecognizeAsync_WithEmptySamples_ReturnsNull()
  {
    var options = Options.Create(new FingerprintingOptions
    {
      SongRec = new SongRecOptions { Enabled = true }
    });
    var service = new SongRecRecognitionService(_loggerMock.Object, options);

    var samples = new AudioSampleBuffer
    {
      Samples = [],
      SampleRate = 44100,
      Channels = 2,
      Duration = TimeSpan.Zero
    };

    var result = await service.RecognizeAsync(samples);

    Assert.Null(result);
  }

  [Fact]
  public void ParseResult_WithValidTrack_ReturnsMetadata()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Just What I Needed",
        Subtitle = "The Cars",
        Images = new SongRecRecognitionService.SongRecImages
        {
          CoverArt = "https://example.com/cover.jpg",
          CoverArtHq = "https://example.com/cover-hq.jpg"
        },
        Sections =
        [
          new SongRecRecognitionService.SongRecSection
          {
            Type = "SONG",
            Metadata =
            [
              new SongRecRecognitionService.SongRecMetadataItem
                { Title = "Album", Text = "The Cars" },
              new SongRecRecognitionService.SongRecMetadataItem
                { Title = "Released", Text = "1978" },
              new SongRecRecognitionService.SongRecMetadataItem
                { Title = "Genre", Text = "Rock" }
            ]
          }
        ]
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("Just What I Needed", metadata.Title);
    Assert.Equal("The Cars", metadata.Artist);
    Assert.Equal("The Cars", metadata.Album);
    Assert.Equal(1978, metadata.ReleaseYear);
    Assert.Equal("Rock", metadata.Genre);
    Assert.Equal("https://example.com/cover-hq.jpg", metadata.CoverArtUrl);
    Assert.Equal(MetadataSource.Shazam, metadata.Source);
  }

  [Fact]
  public void ParseResult_PrefersCoverArtHq_OverCoverArt()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Test Song",
        Subtitle = "Test Artist",
        Images = new SongRecRecognitionService.SongRecImages
        {
          CoverArt = "https://example.com/cover.jpg",
          CoverArtHq = "https://example.com/cover-hq.jpg"
        }
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("https://example.com/cover-hq.jpg", metadata.CoverArtUrl);
  }

  [Fact]
  public void ParseResult_FallsBackToCoverArt_WhenNoHq()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Test Song",
        Subtitle = "Test Artist",
        Images = new SongRecRecognitionService.SongRecImages
        {
          CoverArt = "https://example.com/cover.jpg",
          CoverArtHq = null
        }
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("https://example.com/cover.jpg", metadata.CoverArtUrl);
  }

  [Fact]
  public void ParseResult_WithNoTrack_ReturnsNull()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = null
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.Null(metadata);
  }

  [Fact]
  public void ParseResult_WithNoMetadataSections_StillReturnsMetadata()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = "Minimal Track",
        Subtitle = "Unknown Artist"
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("Minimal Track", metadata.Title);
    Assert.Equal("Unknown Artist", metadata.Artist);
    Assert.Null(metadata.Album);
    Assert.Null(metadata.ReleaseYear);
    Assert.Null(metadata.Genre);
    Assert.Null(metadata.CoverArtUrl);
  }

  [Fact]
  public void ParseResult_WithNullTitle_UsesDefault()
  {
    var result = new SongRecRecognitionService.SongRecResult
    {
      Track = new SongRecRecognitionService.SongRecTrack
      {
        Title = null,
        Subtitle = null
      }
    };

    var metadata = SongRecRecognitionService.ParseResult(result);

    Assert.NotNull(metadata);
    Assert.Equal("Unknown Title", metadata.Title);
    Assert.Equal("Unknown Artist", metadata.Artist);
  }

  private static AudioSampleBuffer CreateTestSamples(double durationSeconds)
  {
    const int sampleRate = 44100;
    const int channels = 2;
    var totalSamples = (int)(sampleRate * channels * durationSeconds);
    var samples = new float[totalSamples];

    // Generate a simple sine wave for test purposes
    for (int i = 0; i < totalSamples; i++)
    {
      var t = (double)i / (sampleRate * channels);
      samples[i] = (float)(Math.Sin(2 * Math.PI * 440 * t) * 0.5);
    }

    return new AudioSampleBuffer
    {
      Samples = samples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = TimeSpan.FromSeconds(durationSeconds)
    };
  }
}
