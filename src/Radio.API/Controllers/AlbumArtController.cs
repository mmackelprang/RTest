using Microsoft.AspNetCore.Mvc;
using Radio.Infrastructure.Audio;

namespace Radio.API.Controllers;

/// <summary>
/// Serves cached album art images. Used by Cast devices (need HTTP URLs)
/// and the Web UI (avoids large base64 data URIs over SignalR).
/// </summary>
[ApiController]
[Route("api/albumart")]
public class AlbumArtController : ControllerBase
{
  private readonly AlbumArtCacheService _cacheService;

  public AlbumArtController(AlbumArtCacheService cacheService)
  {
    _cacheService = cacheService;
  }

  /// <summary>
  /// Gets a cached album art image by filename.
  /// </summary>
  [HttpGet("{filename}")]
  [ResponseCache(Duration = 604800)] // 7 days
  public IActionResult GetImage(string filename)
  {
    var filePath = _cacheService.GetFilePath(filename);
    if (filePath == null)
    {
      return NotFound();
    }

    var extension = Path.GetExtension(filename).TrimStart('.');
    var contentType = AlbumArtCacheService.GetContentTypeFromExtension(extension);

    return PhysicalFile(Path.GetFullPath(filePath), contentType);
  }
}
