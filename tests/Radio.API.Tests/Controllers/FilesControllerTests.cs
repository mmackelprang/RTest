using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Xunit;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for FilesController.
/// </summary>
public class FilesControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public FilesControllerTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory.WithWebHostBuilder(builder =>
    {
      builder.ConfigureServices(services =>
      {
        // Mock IFileBrowser
        var mockFileBrowser = new Mock<IFileBrowser>();
        mockFileBrowser.Setup(m => m.ListFilesAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CreateTestAudioFiles());
        mockFileBrowser.Setup(m => m.GetFileInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((string path, CancellationToken ct) =>
          {
            var files = CreateTestAudioFiles();
            return files.FirstOrDefault(f => f.Path == path);
          });
        mockFileBrowser.Setup(m => m.IsSupportedAudioFile(It.IsAny<string>()))
          .Returns((string path) => path.EndsWith(".mp3") || path.EndsWith(".flac"));
        mockFileBrowser.Setup(m => m.GetSupportedExtensions())
          .Returns(new[] { ".mp3", ".flac", ".wav", ".ogg" });

        services.AddSingleton(mockFileBrowser.Object);
      });
    });

    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetFiles_WithoutParameters_ReturnsFileList()
  {
    // Act
    var response = await _client.GetAsync("/api/files");
    var files = await response.Content.ReadFromJsonAsync<List<AudioFileInfoDto>>();

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.NotNull(files);
    Assert.NotEmpty(files);
  }

  [Fact]
  public async Task GetFiles_WithPath_ReturnsFileList()
  {
    // Act
    var response = await _client.GetAsync("/api/files?path=music");
    var files = await response.Content.ReadFromJsonAsync<List<AudioFileInfoDto>>();

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.NotNull(files);
  }

  [Fact]
  public async Task GetFiles_WithRecursive_ReturnsFileList()
  {
    // Act
    var response = await _client.GetAsync("/api/files?recursive=true");
    var files = await response.Content.ReadFromJsonAsync<List<AudioFileInfoDto>>();

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.NotNull(files);
  }

  [Fact]
  public async Task PlayFile_FileNotActive_ReturnsError()
  {
    // Arrange - File Player not currently active
    var request = new PlayFileRequestDto { Path = "test1.mp3" };

    // Act
    var response = await _client.PostAsJsonAsync("/api/files/play", request);

    // Assert
    // Should return 500 because File Player source is not active
    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
  }

  [Fact]
  public async Task PlayFile_EmptyPath_ReturnsBadRequest()
  {
    // Arrange
    var request = new PlayFileRequestDto { Path = "" };

    // Act
    var response = await _client.PostAsJsonAsync("/api/files/play", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task QueueFiles_EmptyPaths_ReturnsBadRequest()
  {
    // Arrange
    var request = new QueueFilesRequestDto { Paths = new List<string>() };

    // Act
    var response = await _client.PostAsJsonAsync("/api/files/queue", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task QueueFiles_FileNotActive_ReturnsError()
  {
    // Arrange - File Player not currently active
    var request = new QueueFilesRequestDto { Paths = new List<string> { "test1.mp3", "test2.mp3" } };

    // Act
    var response = await _client.PostAsJsonAsync("/api/files/queue", request);

    // Assert
    // Should return 500 because File Player source is not active
    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
  }

  private static List<AudioFileInfo> CreateTestAudioFiles()
  {
    return new List<AudioFileInfo>
    {
      new AudioFileInfo
      {
        Path = "test1.mp3",
        FileName = "test1.mp3",
        Extension = ".mp3",
        SizeBytes = 1024,
        CreatedAt = DateTimeOffset.UtcNow,
        LastModifiedAt = DateTimeOffset.UtcNow,
        Title = "Test Song 1",
        Artist = "Test Artist",
        Album = "Test Album",
        Duration = TimeSpan.FromMinutes(3)
      },
      new AudioFileInfo
      {
        Path = "test2.flac",
        FileName = "test2.flac",
        Extension = ".flac",
        SizeBytes = 2048,
        CreatedAt = DateTimeOffset.UtcNow,
        LastModifiedAt = DateTimeOffset.UtcNow,
        Title = "Test Song 2",
        Artist = "Test Artist",
        Album = "Test Album",
        Duration = TimeSpan.FromMinutes(4)
      }
    };
  }
}
