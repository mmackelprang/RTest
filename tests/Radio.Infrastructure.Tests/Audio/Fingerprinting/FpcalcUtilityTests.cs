using AcoustID;
using AcoustID.Chromaprint;
using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

public class FpcalcUtilityTests
{
  private readonly Mock<ILogger<FpcalcUtility>> _loggerMock;
  private readonly FpcalcUtility _utility;

  public FpcalcUtilityTests()
  {
    _loggerMock = new Mock<ILogger<FpcalcUtility>>();
    _utility = new FpcalcUtility(_loggerMock.Object);
  }

  #region Basic Functionality Tests

  [Fact]
  public async Task GenerateFingerprintsAsync_WithValidSamples_ReturnsFingerprint()
  {
    // Arrange
    var samples = CreateTestSamples(5.0);

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples);

    // Assert
    Assert.NotNull(results);
    Assert.Single(results);
    Assert.NotEmpty(results[0].Fingerprint);
    Assert.True(results[0].DurationSeconds > 0);
    Assert.False(results[0].IsRaw);
  }

  [Fact]
  public async Task GenerateFingerprintsAsync_WithEmptySamples_ReturnsEmptyList()
  {
    // Arrange
    var samples = new AudioSampleBuffer
    {
      Samples = Array.Empty<float>(),
      SampleRate = 44100,
      Channels = 2,
      Duration = TimeSpan.Zero,
      SourceName = "Empty"
    };

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples);

    // Assert
    Assert.NotNull(results);
    Assert.Empty(results);
  }

  [Fact]
  public async Task GenerateSimpleFingerprintAsync_WithValidSamples_ReturnsString()
  {
    // Arrange
    var samples = CreateTestSamples(3.0);

    // Act
    var fingerprint = await _utility.GenerateSimpleFingerprintAsync(samples);

    // Assert
    Assert.NotEmpty(fingerprint);
    Assert.True(IsBase64OrBase64Url(fingerprint));
  }

  #endregion

  #region Chunking Tests

  [Fact]
  public async Task GenerateFingerprintsAsync_WithChunking_ReturnsMultipleResults()
  {
    // Arrange - Create longer audio (chromaprint needs minimum ~1-2s per chunk)
    var samples = CreateTestSamples(15.0);
    var options = new FpcalcOptions
    {
      ChunkDurationSeconds = 4.0
    };

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples, options);

    // Assert
    Assert.NotNull(results);
    Assert.True(results.Count >= 3, $"Expected at least 3 chunks, got {results.Count}");
    Assert.All(results, r => Assert.NotEmpty(r.Fingerprint));
  }

  [Fact]
  public async Task GenerateFingerprintsAsync_WithChunkingAndOverlap_ReturnsResults()
  {
    // Arrange - Create longer audio
    var samples = CreateTestSamples(12.0);
    var options = new FpcalcOptions
    {
      ChunkDurationSeconds = 4.0,
      OverlapChunks = true
    };

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples, options);

    // Assert
    Assert.NotNull(results);
    Assert.True(results.Count >= 2, $"Expected at least 2 chunks, got {results.Count}");
    Assert.All(results, r => Assert.NotEmpty(r.Fingerprint));
  }

  [Fact]
  public async Task GenerateFingerprintsAsync_WithChunkingAndTimestamps_IncludesTimestamps()
  {
    // Arrange
    var samples = CreateTestSamples(9.0);
    var options = new FpcalcOptions
    {
      ChunkDurationSeconds = 3.0,
      IncludeTimestamps = true
    };

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples, options);

    // Assert
    Assert.NotNull(results);
    Assert.True(results.Count >= 2);
    Assert.All(results, r => Assert.True(r.TimestampSeconds.HasValue));
    
    // Verify timestamps increase
    for (int i = 1; i < results.Count; i++)
    {
      Assert.True(results[i].TimestampSeconds > results[i - 1].TimestampSeconds,
        $"Timestamp at index {i} should be greater than previous");
    }
  }

  #endregion

  #region Raw Output Tests

  [Fact]
  public async Task GenerateFingerprintsAsync_WithRawOutput_ReturnsRawFingerprint()
  {
    // Arrange
    var samples = CreateTestSamples(4.0);
    var options = new FpcalcOptions
    {
      RawOutput = true
    };

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples, options);

    // Assert
    Assert.NotNull(results);
    Assert.Single(results);
    Assert.True(results[0].IsRaw);
    Assert.NotEmpty(results[0].Fingerprint);
    Assert.NotNull(results[0].RawSize);
    Assert.True(results[0].RawSize > 0);
    
    // Raw fingerprint should be comma-separated integers
    Assert.Contains(",", results[0].Fingerprint);
  }

  [Fact]
  public async Task GenerateFingerprintsAsync_WithRawSignedOutput_ReturnsSignedIntegers()
  {
    // Arrange
    var samples = CreateTestSamples(4.0);
    var options = new FpcalcOptions
    {
      RawOutput = true,
      SignedOutput = true
    };

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples, options);

    // Assert
    Assert.NotNull(results);
    Assert.Single(results);
    Assert.True(results[0].IsRaw);
    
    // Should contain comma-separated values, possibly with negative signs
    var parts = results[0].Fingerprint.Split(',');
    Assert.True(parts.Length > 0);
    Assert.All(parts, p => Assert.True(int.TryParse(p, out _)));
  }

  #endregion

  #region Max Duration Tests

  [Fact]
  public async Task GenerateFingerprintsAsync_WithMaxDuration_LimitsProcessing()
  {
    // Arrange - create 10 seconds of audio
    var samples = CreateTestSamples(10.0);
    var options = new FpcalcOptions
    {
      MaxDurationSeconds = 5.0 // Only process first 5 seconds
    };

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples, options);

    // Assert
    Assert.NotNull(results);
    Assert.Single(results);
    // Duration should be close to MaxDurationSeconds
    Assert.True(results[0].DurationSeconds <= 10.0);
  }

  [Fact]
  public async Task GenerateFingerprintsAsync_WithMaxDurationAndChunking_LimitsChunks()
  {
    // Arrange - create 20 seconds of audio
    var samples = CreateTestSamples(20.0);
    var options = new FpcalcOptions
    {
      MaxDurationSeconds = 10.0,
      ChunkDurationSeconds = 3.0
    };

    // Act
    var results = await _utility.GenerateFingerprintsAsync(samples, options);

    // Assert
    Assert.NotNull(results);
    // Should have approximately 3-4 chunks (10s / 3s per chunk)
    Assert.True(results.Count <= 5, $"Expected <= 5 chunks for 10s max, got {results.Count}");
  }

  #endregion

  #region Format Tests

  [Fact]
  public void FormatAsJson_WithSingleResult_ReturnsValidJson()
  {
    // Arrange
    var results = new List<FpcalcResult>
    {
      new FpcalcResult
      {
        Fingerprint = "AQAAA0mUaEkSRYnGQ",
        DurationSeconds = 5.5,
        IsRaw = false
      }
    };

    // Act
    var json = _utility.FormatAsJson(results);

    // Assert
    Assert.Contains("duration", json);
    Assert.Contains("fingerprint", json);
    Assert.Contains("5.5", json);
    Assert.Contains("AQAAA0mUaEkSRYnGQ", json);
  }

  [Fact]
  public void FormatAsJson_WithMultipleResults_ReturnsJsonArray()
  {
    // Arrange
    var results = new List<FpcalcResult>
    {
      new FpcalcResult
      {
        Fingerprint = "AQAAA0mUaEkSRYnGQ",
        DurationSeconds = 3.0,
        TimestampSeconds = 0.0,
        IsRaw = false
      },
      new FpcalcResult
      {
        Fingerprint = "BQBBB1nVcFmTSZoHR",
        DurationSeconds = 3.0,
        TimestampSeconds = 3.0,
        IsRaw = false
      }
    };

    // Act
    var json = _utility.FormatAsJson(results);

    // Assert
    Assert.StartsWith("[", json);
    Assert.EndsWith("]", json);
    Assert.Contains("timestamp", json);
  }

  [Fact]
  public void FormatAsJson_WithRawResult_FormatsAsArray()
  {
    // Arrange
    var results = new List<FpcalcResult>
    {
      new FpcalcResult
      {
        Fingerprint = "123,456,789",
        DurationSeconds = 4.0,
        IsRaw = true,
        RawSize = 3
      }
    };

    // Act
    var json = _utility.FormatAsJson(results);

    // Assert
    Assert.Contains("[123,456,789]", json);
  }

  [Fact]
  public void FormatAsText_WithSingleResult_ReturnsTextFormat()
  {
    // Arrange
    var results = new List<FpcalcResult>
    {
      new FpcalcResult
      {
        Fingerprint = "AQAAA0mUaEkSRYnGQ",
        DurationSeconds = 5.5,
        IsRaw = false
      }
    };

    // Act
    var text = _utility.FormatAsText(results);

    // Assert
    // Duration is formatted as integer (rounds to 6)
    Assert.Contains("DURATION=", text);
    Assert.Contains("FINGERPRINT=AQAAA0mUaEkSRYnGQ", text);
  }

  [Fact]
  public void FormatAsText_WithTimestamp_IncludesTimestamp()
  {
    // Arrange
    var results = new List<FpcalcResult>
    {
      new FpcalcResult
      {
        Fingerprint = "AQAAA0mUaEkSRYnGQ",
        DurationSeconds = 3.0,
        TimestampSeconds = 1.5,
        IsRaw = false
      }
    };

    // Act
    var text = _utility.FormatAsText(results);

    // Assert
    Assert.Contains("TIMESTAMP=1.50", text);
    Assert.Contains("DURATION=3", text);
    Assert.Contains("FINGERPRINT=AQAAA0mUaEkSRYnGQ", text);
  }

  [Fact]
  public void FormatAsText_WithMultipleResults_SeparatesWithBlankLines()
  {
    // Arrange
    var results = new List<FpcalcResult>
    {
      new FpcalcResult { Fingerprint = "FP1", DurationSeconds = 3.0, IsRaw = false },
      new FpcalcResult { Fingerprint = "FP2", DurationSeconds = 3.0, IsRaw = false }
    };

    // Act
    var text = _utility.FormatAsText(results);

    // Assert
    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    Assert.True(lines.Length >= 4); // At least 2 results * 2 lines each
  }

  #endregion

  #region Helper Methods

  private static AudioSampleBuffer CreateTestSamples(
    double durationSeconds,
    int sampleRate = 44100,
    int channels = 2,
    double frequency = 440.0)
  {
    var sampleCount = (int)(durationSeconds * sampleRate * channels);
    var samples = new float[sampleCount];

    // Generate sine wave
    for (int i = 0; i < sampleCount; i++)
    {
      var t = (double)i / channels / sampleRate;
      samples[i] = (float)Math.Sin(2 * Math.PI * frequency * t) * 0.5f;
    }

    return new AudioSampleBuffer
    {
      Samples = samples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = TimeSpan.FromSeconds(durationSeconds),
      SourceName = "Test Source"
    };
  }

  private static bool IsBase64OrBase64Url(string str)
  {
    if (string.IsNullOrEmpty(str))
      return false;

    try
    {
      // Try to pad if necessary
      int mod4 = str.Length % 4;
      if (mod4 > 0)
        str += new string('=', 4 - mod4);

      Convert.FromBase64String(str);
      return true;
    }
    catch
    {
      return false;
    }
  }

  #endregion
}
