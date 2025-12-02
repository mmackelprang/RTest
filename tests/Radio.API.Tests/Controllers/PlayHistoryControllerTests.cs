using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Radio.API.Models;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for the PlayHistoryController.
/// </summary>
public class PlayHistoryControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly HttpClient _client;

  public PlayHistoryControllerTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory;
    _client = _factory.CreateClient();
  }

  [Fact]
  public async Task GetRecent_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/playhistory");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var entries = await response.Content.ReadFromJsonAsync<List<PlayHistoryEntryDto>>();
    Assert.NotNull(entries);
  }

  [Fact]
  public async Task GetRecent_WithCount_ReturnsLimitedEntries()
  {
    // Act
    var response = await _client.GetAsync("/api/playhistory?count=5");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
  }

  [Fact]
  public async Task GetToday_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/playhistory/today");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var entries = await response.Content.ReadFromJsonAsync<List<PlayHistoryEntryDto>>();
    Assert.NotNull(entries);
  }

  [Fact]
  public async Task GetByDateRange_ReturnsOk()
  {
    // Arrange
    var start = DateTime.UtcNow.AddDays(-1).ToString("o");
    var end = DateTime.UtcNow.ToString("o");

    // Act
    var response = await _client.GetAsync($"/api/playhistory/range?start={start}&end={end}");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var entries = await response.Content.ReadFromJsonAsync<List<PlayHistoryEntryDto>>();
    Assert.NotNull(entries);
  }

  [Fact]
  public async Task GetBySource_WithValidSource_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/playhistory/source/Vinyl");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var entries = await response.Content.ReadFromJsonAsync<List<PlayHistoryEntryDto>>();
    Assert.NotNull(entries);
  }

  [Fact]
  public async Task GetBySource_WithInvalidSource_ReturnsBadRequest()
  {
    // Act
    var response = await _client.GetAsync("/api/playhistory/source/InvalidSource");

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task GetStatistics_ReturnsOk()
  {
    // Act
    var response = await _client.GetAsync("/api/playhistory/statistics");

    // Assert
    Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

    var stats = await response.Content.ReadFromJsonAsync<PlayStatisticsDto>();
    Assert.NotNull(stats);
    Assert.NotNull(stats.PlaysBySource);
    Assert.NotNull(stats.TopArtists);
    Assert.NotNull(stats.TopTracks);
  }

  [Fact]
  public async Task RecordPlay_WithValidRequest_ReturnsCreated()
  {
    // Arrange
    var uniqueTitle = $"Test Song {Guid.NewGuid()}";
    var request = new RecordPlayRequest
    {
      Source = "Spotify",
      MetadataSource = "Spotify",
      Title = uniqueTitle,
      Artist = "Test Artist",
      Album = "Test Album"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/playhistory", request);

    // Assert
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    var entry = await response.Content.ReadFromJsonAsync<PlayHistoryEntryDto>();
    Assert.NotNull(entry);
    Assert.Equal("Spotify", entry.Source);
    Assert.Equal("Spotify", entry.MetadataSource);
    Assert.True(entry.WasIdentified);
  }

  [Fact]
  public async Task RecordPlay_WithFileTagMetadata_ReturnsCreated()
  {
    // Arrange
    var uniqueTitle = $"File Song {Guid.NewGuid()}";
    var request = new RecordPlayRequest
    {
      Source = "File",
      MetadataSource = "FileTag",
      Title = uniqueTitle,
      Artist = "File Artist"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/playhistory", request);

    // Assert
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    var entry = await response.Content.ReadFromJsonAsync<PlayHistoryEntryDto>();
    Assert.NotNull(entry);
    Assert.Equal("File", entry.Source);
    Assert.Equal("FileTag", entry.MetadataSource);
  }

  [Fact]
  public async Task RecordPlay_WithFingerprintingMetadata_ReturnsCreated()
  {
    // Arrange
    var uniqueTitle = $"Fingerprinted Song {Guid.NewGuid()}";
    var request = new RecordPlayRequest
    {
      Source = "Vinyl",
      MetadataSource = "Fingerprinting",
      Title = uniqueTitle,
      Artist = "Unknown Artist"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/playhistory", request);

    // Assert
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    var entry = await response.Content.ReadFromJsonAsync<PlayHistoryEntryDto>();
    Assert.NotNull(entry);
    Assert.Equal("Vinyl", entry.Source);
    Assert.Equal("Fingerprinting", entry.MetadataSource);
  }

  [Fact]
  public async Task RecordPlay_WithInvalidSource_ReturnsBadRequest()
  {
    // Arrange
    var request = new RecordPlayRequest
    {
      Source = "InvalidSource",
      Title = "Test Song",
      Artist = "Test Artist"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/playhistory", request);

    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task RecordPlay_DuplicateWithinTimeWindow_ReturnsConflict()
  {
    // Arrange
    var uniqueTitle = $"Unique Song {Guid.NewGuid()}";
    var request = new RecordPlayRequest
    {
      Source = "Spotify",
      MetadataSource = "Spotify",
      Title = uniqueTitle,
      Artist = "Test Artist"
    };

    // Act - First record should succeed
    var response1 = await _client.PostAsJsonAsync("/api/playhistory", request);
    Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

    // Act - Second record within time window should be rejected
    var response2 = await _client.PostAsJsonAsync("/api/playhistory", request);

    // Assert
    Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
  }

  [Fact]
  public async Task GetById_WithNonExistentId_ReturnsNotFound()
  {
    // Act
    var response = await _client.GetAsync("/api/playhistory/nonexistent-id");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Delete_WithNonExistentId_ReturnsNotFound()
  {
    // Act
    var response = await _client.DeleteAsync("/api/playhistory/nonexistent-id");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task RecordAndRetrievePlay_RoundTripWorks()
  {
    // Arrange
    var uniqueTitle = $"RoundTrip Song {Guid.NewGuid()}";
    var request = new RecordPlayRequest
    {
      Source = "File",
      MetadataSource = "FileTag",
      Title = uniqueTitle,
      Artist = "RoundTrip Artist",
      Album = "RoundTrip Album",
      DurationSeconds = 180
    };

    // Act - Record
    var createResponse = await _client.PostAsJsonAsync("/api/playhistory", request);
    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

    var created = await createResponse.Content.ReadFromJsonAsync<PlayHistoryEntryDto>();
    Assert.NotNull(created);

    // Act - Retrieve
    var getResponse = await _client.GetAsync($"/api/playhistory/{created.Id}");
    Assert.True(getResponse.IsSuccessStatusCode);

    var retrieved = await getResponse.Content.ReadFromJsonAsync<PlayHistoryEntryDto>();

    // Assert
    Assert.NotNull(retrieved);
    Assert.Equal(created.Id, retrieved.Id);
    Assert.Equal("File", retrieved.Source);
    Assert.Equal("FileTag", retrieved.MetadataSource);
    Assert.NotNull(retrieved.Track);
    Assert.Equal(uniqueTitle, retrieved.Track.Title);
    Assert.Equal("RoundTrip Artist", retrieved.Track.Artist);
    Assert.Equal("RoundTrip Album", retrieved.Track.Album);
  }

  [Fact]
  public async Task RecordAndDeletePlay_Works()
  {
    // Arrange
    var uniqueTitle = $"DeleteTest Song {Guid.NewGuid()}";
    var request = new RecordPlayRequest
    {
      Source = "Radio",
      Title = uniqueTitle,
      Artist = "Delete Artist"
    };

    // Act - Record
    var createResponse = await _client.PostAsJsonAsync("/api/playhistory", request);
    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

    var created = await createResponse.Content.ReadFromJsonAsync<PlayHistoryEntryDto>();
    Assert.NotNull(created);

    // Act - Delete
    var deleteResponse = await _client.DeleteAsync($"/api/playhistory/{created.Id}");
    Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

    // Act - Verify deleted
    var getResponse = await _client.GetAsync($"/api/playhistory/{created.Id}");
    Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
  }
}
