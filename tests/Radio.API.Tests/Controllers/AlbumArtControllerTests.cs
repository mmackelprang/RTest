using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Radio.API.Controllers;
using Radio.Infrastructure.Audio;

namespace Radio.API.Tests.Controllers;

public class AlbumArtControllerTests : IDisposable
{
  private readonly AlbumArtCacheService _cacheService;
  private readonly AlbumArtController _controller;

  public AlbumArtControllerTests()
  {
    var logger = new Mock<ILogger<AlbumArtCacheService>>().Object;
    _cacheService = new AlbumArtCacheService(logger);
    _controller = new AlbumArtController(_cacheService);
  }

  public void Dispose()
  {
    _cacheService.Dispose();
    // Clean up test files
    var cacheDir = Path.Combine(".", "data", "albumart");
    if (Directory.Exists(cacheDir))
    {
      foreach (var file in Directory.EnumerateFiles(cacheDir))
      {
        var info = new FileInfo(file);
        if ((DateTime.UtcNow - info.CreationTimeUtc).TotalMinutes < 1)
        {
          try { info.Delete(); } catch { }
        }
      }
    }
  }

  [Fact]
  public async Task GetImage_ExistingFile_ReturnsPhysicalFile()
  {
    // Arrange: save an image to the cache
    var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
    var url = await _cacheService.SaveAsync(imageData, "image/jpeg");
    var filename = url.Split('/').Last();

    // Act
    var result = _controller.GetImage(filename);

    // Assert
    Assert.IsType<PhysicalFileResult>(result);
    var fileResult = (PhysicalFileResult)result;
    Assert.Equal("image/jpeg", fileResult.ContentType);
  }

  [Fact]
  public async Task GetImage_PngFile_ReturnsCorrectContentType()
  {
    var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    var url = await _cacheService.SaveAsync(imageData, "image/png");
    var filename = url.Split('/').Last();

    var result = _controller.GetImage(filename);

    Assert.IsType<PhysicalFileResult>(result);
    var fileResult = (PhysicalFileResult)result;
    Assert.Equal("image/png", fileResult.ContentType);
  }

  [Fact]
  public void GetImage_MissingFile_Returns404()
  {
    var result = _controller.GetImage("nonexistent.jpg");

    Assert.IsType<NotFoundResult>(result);
  }

  [Fact]
  public void GetImage_PathTraversal_Returns404()
  {
    var result = _controller.GetImage("../../../etc/passwd");

    Assert.IsType<NotFoundResult>(result);
  }
}
