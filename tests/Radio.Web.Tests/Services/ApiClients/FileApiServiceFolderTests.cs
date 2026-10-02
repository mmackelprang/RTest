using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services.ApiClients;

/// <summary>UI-32: the file API client's folder listing and its cancellation behaviour.</summary>
public class FileApiServiceFolderTests
{
  private readonly RoutedApiHandler _api = new();

  private FileApiService Client() =>
    new(new HttpClient(_api, disposeHandler: false) { BaseAddress = new Uri("http://api.test") },
      NullLogger<FileApiService>.Instance);

  [Fact]
  public async Task GetFolderTracks_SendsThePathAndBothSwitches()
  {
    _api.Get("/api/files/folder-tracks", new { folderPath = "/m/ABBA", folderName = "ABBA", paths = new[] { "/m/ABBA/1.mp3" } });

    var (folder, error) = await Client().GetFolderTracksAsync("/m/ABBA", includeSubfolders: false, checkReadable: true);

    error.Should().BeNull();
    folder!.Paths.Should().Equal("/m/ABBA/1.mp3");
    var query = Uri.UnescapeDataString(_api.Requests.Single().Query);
    query.Should().Contain("includeSubfolders=false").And.Contain("checkReadable=true").And.Contain("path=/m/ABBA");
  }

  [Fact]
  public async Task GetFolderTracks_NullPath_SendsNoPath()
  {
    _api.Get("/api/files/folder-tracks", new { folderPath = "/m", folderName = "m" });

    await Client().GetFolderTracksAsync(null, includeSubfolders: true, checkReadable: false);

    _api.Requests.Single().Query.Should().NotContain("path=");
  }

  [Fact]
  public async Task GetFolderTracks_AnErrorBody_IsReturnedAsTheMessage()
  {
    _api.Route(HttpMethod.Get, "/api/files/folder-tracks", HttpStatusCode.BadRequest,
      JsonSerializer.Serialize(new { error = "Folder is not within an allowed directory" }));

    var (folder, error) = await Client().GetFolderTracksAsync("/etc", true, true);

    folder.Should().BeNull();
    error.Should().Be("Folder is not within an allowed directory");
  }

  [Fact]
  public async Task AddFilesToQueue_CallerCancellation_Throws_RatherThanReportingAFailure()
  {
    // Add folder's dialog closing mid-run cancels the batch; that is not an error to toast or log.
    _api.Post("/api/files/queue");
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await FluentActions.Awaiting(() => Client().AddFilesToQueueAsync(new List<string> { "/m/1.mp3" }, cts.Token))
      .Should().ThrowAsync<OperationCanceledException>();
  }
}
