using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Radio.API.Tests.TestSupport;

/// <summary>
/// Regression guard for TEST-3. Every other test in this project passes whether or not the hosts
/// share storage — that is exactly why the defect survived: the shared-SQLite collision only
/// surfaces under full-suite parallel load, as an <c>InternalServerError</c> from whichever test
/// happened to run first in its class. These assert the isolation directly, so removing it fails
/// here immediately instead of intermittently in CI weeks later.
/// </summary>
public class StorageIsolationTests
{
  [Fact]
  public void EachFactory_GetsItsOwnStorageRoot()
  {
    using var first = new CustomWebApplicationFactory<Program>();
    using var second = new CustomWebApplicationFactory<Program>();

    Assert.NotEqual(first.StorageRoot, second.StorageRoot);
  }

  [Fact]
  public void StorageRoot_IsNotTheSharedWorkingDirectory()
  {
    using var factory = new CustomWebApplicationFactory<Program>();

    // "./data" relative to the process working directory is the shared location every host used
    // before TEST-3. Landing back there is the regression this guards.
    string shared = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data"));
    string isolated = Path.GetFullPath(Path.Combine(factory.StorageRoot, "data"));

    Assert.NotEqual(shared, isolated);
  }

  [Fact]
  public void ConfiguredDatabaseRoot_PointsInsideTheIsolatedRoot()
  {
    using var factory = new CustomWebApplicationFactory<Program>();

    // Force the host to build so configuration is materialised.
    using var client = factory.CreateClient();
    IConfiguration configuration = factory.Services.GetRequiredService<IConfiguration>();

    string? root = configuration["Database:RootPath"];

    Assert.False(string.IsNullOrEmpty(root));
    Assert.StartsWith(factory.StorageRoot, root, StringComparison.OrdinalIgnoreCase);
  }
}
