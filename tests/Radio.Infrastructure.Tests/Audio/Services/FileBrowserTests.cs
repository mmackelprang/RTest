using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Services;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// Unit tests for FileBrowser service.
/// </summary>
public class FileBrowserTests : IDisposable
{
  private readonly Mock<ILogger<FileBrowser>> _mockLogger;
  private readonly Mock<IOptionsMonitor<FilePlayerOptions>> _mockOptions;
  private readonly string _testRootDir;
  private readonly string _testAudioDir;
  private readonly FileBrowser _fileBrowser;

  public FileBrowserTests()
  {
    _mockLogger = new Mock<ILogger<FileBrowser>>();
    _mockOptions = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    
    // Create temporary test directory
    _testRootDir = Path.Combine(Path.GetTempPath(), $"radio-test-{Guid.NewGuid()}");
    _testAudioDir = Path.Combine(_testRootDir, "audio");
    Directory.CreateDirectory(_testAudioDir);

    // Configure options
    _mockOptions.Setup(m => m.CurrentValue).Returns(new FilePlayerOptions
    {
      RootDirectory = "audio"
    });

    _fileBrowser = new FileBrowser(_mockLogger.Object, _mockOptions.Object, _testRootDir);
  }

  public void Dispose()
  {
    // Clean up test directory
    if (Directory.Exists(_testRootDir))
    {
      Directory.Delete(_testRootDir, true);
    }
  }

  [Fact]
  public async Task ListFilesAsync_EmptyDirectory_ReturnsEmptyList()
  {
    // Act
    var result = await _fileBrowser.ListFilesAsync();

    // Assert
    Assert.Empty(result);
  }

  [Fact]
  public async Task ListFilesAsync_WithAudioFiles_ReturnsFiles()
  {
    // Arrange
    CreateTestAudioFile("test1.mp3");
    CreateTestAudioFile("test2.flac");
    CreateTestAudioFile("test3.wav");

    // Act
    var result = await _fileBrowser.ListFilesAsync();

    // Assert
    Assert.Equal(3, result.Count);
    Assert.Contains(result, f => f.FileName == "test1.mp3");
    Assert.Contains(result, f => f.FileName == "test2.flac");
    Assert.Contains(result, f => f.FileName == "test3.wav");
  }

  [Fact]
  public async Task ListFilesAsync_WithNonAudioFiles_ExcludesNonAudioFiles()
  {
    // Arrange
    CreateTestAudioFile("test.mp3");
    CreateTestFile("test.txt");
    CreateTestFile("test.jpg");

    // Act
    var result = await _fileBrowser.ListFilesAsync();

    // Assert
    Assert.Single(result);
    Assert.Equal("test.mp3", result[0].FileName);
  }

  [Fact]
  public async Task ListFilesAsync_WithSubdirectory_NonRecursive_ReturnsOnlyRootFiles()
  {
    // Arrange
    CreateTestAudioFile("root.mp3");
    var subDir = Path.Combine(_testAudioDir, "subdir");
    Directory.CreateDirectory(subDir);
    CreateTestAudioFile("subdir/sub.mp3");

    // Act
    var result = await _fileBrowser.ListFilesAsync(recursive: false);

    // Assert
    Assert.Single(result);
    Assert.Equal("root.mp3", result[0].FileName);
  }

  [Fact]
  public async Task ListFilesAsync_WithSubdirectory_Recursive_ReturnsAllFiles()
  {
    // Arrange
    CreateTestAudioFile("root.mp3");
    var subDir = Path.Combine(_testAudioDir, "subdir");
    Directory.CreateDirectory(subDir);
    CreateTestAudioFile("subdir/sub.mp3");

    // Act
    var result = await _fileBrowser.ListFilesAsync(recursive: true);

    // Assert
    Assert.Equal(2, result.Count);
    Assert.Contains(result, f => f.FileName == "root.mp3");
    Assert.Contains(result, f => f.FileName == "sub.mp3");
  }

  [Fact]
  public async Task ListFilesAsync_WithPath_ListsFilesInPath()
  {
    // Arrange
    CreateTestAudioFile("root.mp3");
    var subDir = Path.Combine(_testAudioDir, "subdir");
    Directory.CreateDirectory(subDir);
    CreateTestAudioFile("subdir/sub.mp3");

    // Act
    var result = await _fileBrowser.ListFilesAsync("subdir");

    // Assert
    Assert.Single(result);
    Assert.Equal("sub.mp3", result[0].FileName);
  }

  [Fact]
  public async Task ListFilesAsync_NonExistentPath_ReturnsEmptyList()
  {
    // Act
    var result = await _fileBrowser.ListFilesAsync("nonexistent");

    // Assert
    Assert.Empty(result);
  }

  [Fact]
  public async Task GetFileInfoAsync_ExistingFile_ReturnsFileInfo()
  {
    // Arrange
    var filePath = CreateTestAudioFile("test.mp3");

    // Act
    var result = await _fileBrowser.GetFileInfoAsync("test.mp3");

    // Assert
    Assert.NotNull(result);
    Assert.Equal("test.mp3", result.FileName);
    Assert.Equal(".mp3", result.Extension);
    Assert.True(result.SizeBytes > 0);
  }

  [Fact]
  public async Task GetFileInfoAsync_NonExistentFile_ReturnsNull()
  {
    // Act
    var result = await _fileBrowser.GetFileInfoAsync("nonexistent.mp3");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetFileInfoAsync_NonAudioFile_ReturnsNull()
  {
    // Arrange
    CreateTestFile("test.txt");

    // Act
    var result = await _fileBrowser.GetFileInfoAsync("test.txt");

    // Assert
    Assert.Null(result);
  }

  [Theory]
  [InlineData("test.mp3", true)]
  [InlineData("test.flac", true)]
  [InlineData("test.wav", true)]
  [InlineData("test.ogg", true)]
  [InlineData("test.aac", true)]
  [InlineData("test.m4a", true)]
  [InlineData("test.wma", true)]
  [InlineData("test.txt", false)]
  [InlineData("test.jpg", false)]
  [InlineData("test.doc", false)]
  public void IsSupportedAudioFile_VariousExtensions_ReturnsExpected(string fileName, bool expected)
  {
    // Arrange
    var filePath = Path.Combine(_testAudioDir, fileName);

    // Act
    var result = _fileBrowser.IsSupportedAudioFile(filePath);

    // Assert
    Assert.Equal(expected, result);
  }

  [Fact]
  public void GetSupportedExtensions_ReturnsExpectedExtensions()
  {
    // Act
    var result = _fileBrowser.GetSupportedExtensions();

    // Assert
    Assert.Contains(".mp3", result);
    Assert.Contains(".flac", result);
    Assert.Contains(".wav", result);
    Assert.Contains(".ogg", result);
    Assert.Contains(".aac", result);
    Assert.Contains(".m4a", result);
    Assert.Contains(".wma", result);
  }

  [Fact]
  public async Task ListFilesAsync_FileInfo_ContainsExpectedMetadata()
  {
    // Arrange
    CreateTestAudioFile("test.mp3");

    // Act
    var result = await _fileBrowser.ListFilesAsync();

    // Assert
    Assert.Single(result);
    var fileInfo = result[0];
    Assert.Equal("test.mp3", fileInfo.FileName);
    Assert.Equal(".mp3", fileInfo.Extension);
    Assert.True(fileInfo.SizeBytes > 0);
    Assert.NotEqual(DateTimeOffset.MinValue, fileInfo.CreatedAt);
    Assert.NotEqual(DateTimeOffset.MinValue, fileInfo.LastModifiedAt);
    // Title defaults to filename without extension when no metadata
    Assert.NotNull(fileInfo.Title);
    Assert.Equal("test", fileInfo.Title);
  }

  private string CreateTestAudioFile(string relativePath)
  {
    var fullPath = Path.Combine(_testAudioDir, relativePath);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }
    // Create minimal MP3 file with ID3v2 header (simulated)
    File.WriteAllBytes(fullPath, new byte[] { 0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00 });
    return fullPath;
  }

  private string CreateTestFile(string relativePath)
  {
    var fullPath = Path.Combine(_testAudioDir, relativePath);
    File.WriteAllText(fullPath, "test content");
    return fullPath;
  }
}
