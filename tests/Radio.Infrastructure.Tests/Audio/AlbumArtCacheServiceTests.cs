using Microsoft.Extensions.Logging;
using Moq;
using Radio.Infrastructure.Audio;

namespace Radio.Infrastructure.Tests.Audio;

public class AlbumArtCacheServiceTests : IDisposable
{
  private readonly AlbumArtCacheService _service;
  private readonly string _testCacheDir;

  public AlbumArtCacheServiceTests()
  {
    // Use a unique temp directory per test instance to avoid conflicts
    _testCacheDir = Path.Combine(Path.GetTempPath(), $"albumart_test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testCacheDir);

    var logger = new Mock<ILogger<AlbumArtCacheService>>().Object;
    _service = new AlbumArtCacheService(logger);
  }

  public void Dispose()
  {
    _service.Dispose();
    // Clean up test cache dir (the default ./data/albumart dir)
    try
    {
      var defaultDir = Path.Combine(".", "data", "albumart");
      if (Directory.Exists(defaultDir))
      {
        foreach (var file in Directory.EnumerateFiles(defaultDir, "*.jpg")
          .Concat(Directory.EnumerateFiles(defaultDir, "*.png")))
        {
          // Only delete files we created in this test (recent files)
          var info = new FileInfo(file);
          if ((DateTime.UtcNow - info.CreationTimeUtc).TotalMinutes < 1)
          {
            try { info.Delete(); } catch { }
          }
        }
      }
    }
    catch { }
  }

  [Fact]
  public async Task SaveAsync_CreatesFile_ReturnsRelativeUrl()
  {
    var imageData = CreateTestJpegBytes();
    var result = await _service.SaveAsync(imageData, "image/jpeg");

    Assert.StartsWith("/api/albumart/", result);
    Assert.EndsWith(".jpg", result);
  }

  [Fact]
  public async Task SaveAsync_PngMimeType_ReturnsPngExtension()
  {
    var imageData = CreateTestPngBytes();
    var result = await _service.SaveAsync(imageData, "image/png");

    Assert.EndsWith(".png", result);
  }

  [Fact]
  public async Task SaveAsync_SameContent_ReturnsSameUrl()
  {
    var imageData = CreateTestJpegBytes();

    var result1 = await _service.SaveAsync(imageData, "image/jpeg");
    var result2 = await _service.SaveAsync(imageData, "image/jpeg");

    Assert.Equal(result1, result2);
  }

  [Fact]
  public async Task SaveAsync_DifferentContent_ReturnsDifferentUrls()
  {
    var imageData1 = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02 };
    var imageData2 = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x03, 0x04 };

    var result1 = await _service.SaveAsync(imageData1, "image/jpeg");
    var result2 = await _service.SaveAsync(imageData2, "image/jpeg");

    Assert.NotEqual(result1, result2);
  }

  [Fact]
  public async Task GetFilePath_ExistingFile_ReturnsPath()
  {
    var imageData = CreateTestJpegBytes();
    var url = await _service.SaveAsync(imageData, "image/jpeg");
    var filename = url.Split('/').Last();

    var path = _service.GetFilePath(filename);

    Assert.NotNull(path);
    Assert.True(File.Exists(path));
  }

  [Fact]
  public void GetFilePath_MissingFile_ReturnsNull()
  {
    var path = _service.GetFilePath("nonexistent.jpg");

    Assert.Null(path);
  }

  [Fact]
  public void GetFilePath_PathTraversal_ReturnsNull()
  {
    Assert.Null(_service.GetFilePath("../../../etc/passwd"));
    Assert.Null(_service.GetFilePath("..\\..\\secrets.json"));
  }

  [Fact]
  public void GetFilePath_EmptyFilename_ReturnsNull()
  {
    Assert.Null(_service.GetFilePath(""));
    Assert.Null(_service.GetFilePath(null!));
  }

  [Fact]
  public void CleanupExpired_RemovesOldFiles_PreservesRecent()
  {
    // Create files directly in the cache dir for testing cleanup
    var cacheDir = Path.Combine(".", "data", "albumart");
    Directory.CreateDirectory(cacheDir);

    var oldFile = Path.Combine(cacheDir, "old_test_cleanup.jpg");
    var recentFile = Path.Combine(cacheDir, "recent_test_cleanup.jpg");

    File.WriteAllBytes(oldFile, new byte[] { 0xFF, 0xD8 });
    File.WriteAllBytes(recentFile, new byte[] { 0xFF, 0xD8 });

    // Set old file to 8 days ago
    File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-8));

    _service.CleanupExpired();

    Assert.False(File.Exists(oldFile));
    Assert.True(File.Exists(recentFile));

    // Cleanup
    try { File.Delete(recentFile); } catch { }
  }

  [Fact]
  public void DetectMimeFromBytes_Png_ReturnsCorrectMime()
  {
    var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    Assert.Equal("image/png", AlbumArtCacheService.DetectMimeFromBytes(pngBytes));
  }

  [Fact]
  public void DetectMimeFromBytes_Jpeg_ReturnsCorrectMime()
  {
    var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
    Assert.Equal("image/jpeg", AlbumArtCacheService.DetectMimeFromBytes(jpegBytes));
  }

  [Fact]
  public void DetectMimeFromBytes_Unknown_ReturnsNull()
  {
    var unknownBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };
    Assert.Null(AlbumArtCacheService.DetectMimeFromBytes(unknownBytes));
  }

  [Fact]
  public void DetectMimeFromBytes_TooShort_ReturnsNull()
  {
    Assert.Null(AlbumArtCacheService.DetectMimeFromBytes(new byte[] { 0xFF }));
  }

  [Fact]
  public void GetContentTypeFromExtension_ReturnsCorrectTypes()
  {
    Assert.Equal("image/jpeg", AlbumArtCacheService.GetContentTypeFromExtension("jpg"));
    Assert.Equal("image/jpeg", AlbumArtCacheService.GetContentTypeFromExtension("jpeg"));
    Assert.Equal("image/png", AlbumArtCacheService.GetContentTypeFromExtension("png"));
    Assert.Equal("image/webp", AlbumArtCacheService.GetContentTypeFromExtension("webp"));
    Assert.Equal("application/octet-stream", AlbumArtCacheService.GetContentTypeFromExtension("bmp"));
  }

  private static byte[] CreateTestJpegBytes()
  {
    // Minimal JPEG-like bytes (not valid JPEG, but enough for our tests)
    return new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
  }

  private static byte[] CreateTestPngBytes()
  {
    // PNG magic bytes
    return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
  }
}
