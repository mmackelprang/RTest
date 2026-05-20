using System.Net;
using System.Net.Http.Json;
using Radio.API.Models;
using Radio.API.Tests.TestSupport;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// Integration tests for HealthController. The /api/health/version endpoint is
/// load-bearing for deploy verification — the deploy scripts curl it and fail
/// if the returned GitSha doesn't match the locally-built commit.
/// </summary>
public class HealthControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  private readonly HttpClient _client;

  public HealthControllerTests(CustomWebApplicationFactory<Program> factory)
  {
    _client = factory.CreateClient();
  }

  [Fact]
  public async Task GetVersion_ReturnsOk()
  {
    var response = await _client.GetAsync("/api/health/version");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task GetVersion_ReturnsAssemblyMetadata()
  {
    var info = await _client.GetFromJsonAsync<VersionInfoDto>("/api/health/version");

    Assert.NotNull(info);
    Assert.Equal("Radio.API", info!.AssemblyName);
    Assert.False(string.IsNullOrEmpty(info.InformationalVersion));
    Assert.False(string.IsNullOrEmpty(info.AssemblyVersion));
    Assert.False(string.IsNullOrEmpty(info.GitSha));
    // Short SHA should be 7 chars when full SHA is present, otherwise mirrors GitSha
    Assert.True(info.GitShaShort.Length <= info.GitSha.Length);
  }
}
