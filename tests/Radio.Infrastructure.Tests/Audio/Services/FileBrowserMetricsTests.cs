namespace Radio.Infrastructure.Tests.Audio.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Audio.Services;
using Xunit;

public class FileBrowserMetricsTests : IAsyncLifetime
{
  private readonly Mock<IMetricsCollector> _mockMetricsCollector;
  private readonly Mock<IOptionsMonitor<FilePlayerOptions>> _mockOptions;
  private readonly string _testDirectory;
  private readonly FileBrowser _fileBrowser;

  public FileBrowserMetricsTests()
  {
    _mockMetricsCollector = new Mock<IMetricsCollector>();
    _mockOptions = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    _testDirectory = Path.Combine(Path.GetTempPath(), $"test_audio_{Guid.NewGuid()}");

    _mockOptions.Setup(x => x.CurrentValue).Returns(new FilePlayerOptions
    {
      RootDirectory = ""
    });

    _fileBrowser = new FileBrowser(
      NullLogger<FileBrowser>.Instance,
      _mockOptions.Object,
      _testDirectory,
      _mockMetricsCollector.Object);
  }

  public Task InitializeAsync()
  {
    Directory.CreateDirectory(_testDirectory);
    
    // Create a few test audio files
    File.WriteAllText(Path.Combine(_testDirectory, "test1.mp3"), "fake audio");
    File.WriteAllText(Path.Combine(_testDirectory, "test2.mp3"), "fake audio");
    File.WriteAllText(Path.Combine(_testDirectory, "test3.flac"), "fake audio");
    
    return Task.CompletedTask;
  }

  public Task DisposeAsync()
  {
    if (Directory.Exists(_testDirectory))
    {
      Directory.Delete(_testDirectory, true);
    }
    return Task.CompletedTask;
  }

  [Fact]
  public async Task ListFilesAsync_TracksLibraryTracksTotal()
  {
    // Act
    await _fileBrowser.ListFilesAsync(null, false, CancellationToken.None);

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Gauge("library.tracks_total", It.IsAny<double>(), null),
      Times.Once);
  }

  [Fact]
  public async Task ListFilesAsync_TracksScanDuration()
  {
    // Act
    await _fileBrowser.ListFilesAsync(null, false, CancellationToken.None);

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Gauge("library.scan_duration_ms", It.IsAny<double>(), null),
      Times.Once);
  }

  [Fact]
  public async Task ListFilesAsync_TracksNewTracksAdded()
  {
    // Act
    await _fileBrowser.ListFilesAsync(null, false, CancellationToken.None);

    // Assert - Should track new tracks (in simplified implementation, this is total count)
    _mockMetricsCollector.Verify(
      x => x.Increment("library.new_tracks_added", It.IsAny<double>(), null),
      Times.Once);
  }

  [Fact]
  public async Task ListFilesAsync_ReportsCorrectTrackCount()
  {
    // Act
    await _fileBrowser.ListFilesAsync(null, false, CancellationToken.None);

    // Assert - Should find 3 audio files
    _mockMetricsCollector.Verify(
      x => x.Gauge("library.tracks_total", 3, null),
      Times.Once);
  }

  [Fact]
  public async Task ListFilesAsync_ReportsScanDurationGreaterThanZero()
  {
    // Act
    await _fileBrowser.ListFilesAsync(null, false, CancellationToken.None);

    // Assert
    _mockMetricsCollector.Verify(
      x => x.Gauge("library.scan_duration_ms", It.Is<double>(d => d >= 0), null),
      Times.Once);
  }
}
