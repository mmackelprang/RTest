namespace Radio.Web.Tests.Configuration;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Web.Configuration;

/// <summary>
/// Pins the DataProtection key-ring wiring for Radio.Web. Regression guard for the
/// 2026-08-16 outage: Radio.Web configured no key-ring path at all, so ASP.NET Core
/// fell back to <c>$HOME/.aspnet/DataProtection-Keys</c> — which
/// <c>radio-web.service</c>'s <c>ProtectHome=true</c> masks as a read-only tmpfs.
/// Minting a key threw <c>IOException: Read-only file system</c>, and because Blazor
/// Server protects the marker it emits for every interactive component, every page
/// returned HTTP 500. See design/plans/SECRET-KEYRING-INVESTIGATION.md.
///
/// The property these tests defend is that the resolved path depends only on
/// configuration and the caller-supplied base directory — never on <c>HOME</c>.
/// </summary>
public class DataProtectionSetupTests : IDisposable
{
  private readonly string _testDirectory;

  public DataProtectionSetupTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), $"WebDataProtectionTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_testDirectory);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_testDirectory))
      {
        Directory.Delete(_testDirectory, recursive: true);
      }
    }
    catch { /* cleanup best-effort */ }

    GC.SuppressFinalize(this);
  }

  private static IConfiguration BuildConfiguration(params (string Key, string Value)[] entries) =>
    new ConfigurationBuilder()
      .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
      .Build();

  /// <summary>
  /// An absolute base directory this test run can actually use. Linux-shaped paths
  /// such as "/opt/radio-console" are rooted but NOT fully qualified on Windows, and
  /// <see cref="Path.GetFullPath(string, string)"/> rejects a base path that is not
  /// fully qualified — so the box's real layout has to be mirrored per-OS.
  /// </summary>
  private static string BoxWorkingDirectory =>
    OperatingSystem.IsWindows() ? @"C:\opt\radio-console" : "/opt/radio-console";

  [Fact]
  public void ResolveKeysPath_UsesExplicitKeysPath_RelativeToBaseDirectory()
  {
    var configuration = BuildConfiguration(
      ("DataProtection:KeysPath", "./data/keys-web"),
      ("Database:RootPath", "./should-be-ignored"));

    var resolved = DataProtectionSetup.ResolveKeysPath(configuration, _testDirectory);

    Assert.Equal(Path.Combine(_testDirectory, "data", "keys-web"), resolved);
  }

  [Fact]
  public void ResolveKeysPath_ProductionShape_LandsUnderTheWorkingDirectory()
  {
    // The shipped src/Radio.Web/appsettings.json value, resolved against the working
    // directory systemd gives the unit (WorkingDirectory=/opt/radio-console). This is
    // the case that matters: ASPNETCORE_CONTENTROOT points at .../web instead, and
    // resolving there would put the ring somewhere the deploy's `rsync --delete`
    // wipes and outside the data/ path the unit grants write access to.
    var configuration = BuildConfiguration(("DataProtection:KeysPath", "./data/keys-web"));

    var resolved = DataProtectionSetup.ResolveKeysPath(configuration, BoxWorkingDirectory);

    Assert.Equal(Path.Combine(BoxWorkingDirectory, "data", "keys-web"), resolved);
  }

  [Fact]
  public void ResolveKeysPath_HonoursAbsoluteKeysPath_IgnoringBaseDirectory()
  {
    var absolute = Path.Combine(_testDirectory, "elsewhere", "ring");
    var configuration = BuildConfiguration(("DataProtection:KeysPath", absolute));

    var resolved = DataProtectionSetup.ResolveKeysPath(configuration, BoxWorkingDirectory);

    Assert.Equal(absolute, resolved);
  }

  [Fact]
  public void ResolveKeysPath_FallsBackToDatabaseRootPath_WhenKeysPathAbsent()
  {
    var configuration = BuildConfiguration(("Database:RootPath", "./custom-data"));

    var resolved = DataProtectionSetup.ResolveKeysPath(configuration, _testDirectory);

    Assert.Equal(Path.Combine(_testDirectory, "custom-data", "keys-web"), resolved);
  }

  [Fact]
  public void ResolveKeysPath_FallsBackToDataKeysWeb_WhenNothingConfigured()
  {
    var configuration = BuildConfiguration();

    var resolved = DataProtectionSetup.ResolveKeysPath(configuration, _testDirectory);

    Assert.Equal(Path.Combine(_testDirectory, "data", "keys-web"), resolved);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void ResolveKeysPath_TreatsBlankKeysPathAsAbsent(string blank)
  {
    var configuration = BuildConfiguration(
      ("DataProtection:KeysPath", blank),
      ("Database:RootPath", "./custom-data"));

    var resolved = DataProtectionSetup.ResolveKeysPath(configuration, _testDirectory);

    Assert.Equal(Path.Combine(_testDirectory, "custom-data", "keys-web"), resolved);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void ResolveKeysPath_TreatsBlankDatabaseRootPathAsAbsent(string blank)
  {
    var configuration = BuildConfiguration(("Database:RootPath", blank));

    var resolved = DataProtectionSetup.ResolveKeysPath(configuration, _testDirectory);

    Assert.Equal(Path.Combine(_testDirectory, "data", "keys-web"), resolved);
  }

  [Fact]
  public void ResolveKeysPath_DoesNotUseTheApiSecretsRingDirectory()
  {
    // The API's ring is <data>/keys (src/Radio.API/appsettings.json). Radio.Web must
    // resolve somewhere else so the ring that encrypts stored secrets only ever
    // accumulates keys the API created — key files on disk carry no app name, so a
    // shared directory would leave a future secrets investigation unable to
    // attribute them.
    var configuration = BuildConfiguration(("Database:RootPath", "./data"));

    var resolved = DataProtectionSetup.ResolveKeysPath(configuration, _testDirectory);

    Assert.NotEqual(Path.Combine(_testDirectory, "data", "keys"), resolved);
  }

  [Fact]
  public void AddRadioWebDataProtection_CreatesTheKeyRingDirectory()
  {
    var keysPath = Path.Combine(_testDirectory, "not", "yet", "created");
    Assert.False(Directory.Exists(keysPath));

    new ServiceCollection().AddRadioWebDataProtection(keysPath);

    Assert.True(Directory.Exists(keysPath));
  }

  [Fact]
  public void AddRadioWebDataProtection_PersistsKeysToTheConfiguredDirectory()
  {
    // The end-to-end property the outage violated: protecting a payload must write
    // its key under the configured path, not under $HOME/.aspnet/DataProtection-Keys.
    var keysPath = Path.Combine(_testDirectory, "keys-web");
    var services = new ServiceCollection();
    services.AddRadioWebDataProtection(keysPath);

    using var provider = services.BuildServiceProvider();
    var protector = provider.GetRequiredService<IDataProtectionProvider>()
      .CreateProtector("DataProtectionSetupTests");

    var roundTripped = protector.Unprotect(protector.Protect("marker"));

    Assert.Equal("marker", roundTripped);
    Assert.NotEmpty(Directory.GetFiles(keysPath, "key-*.xml"));
  }

  [Fact]
  public void AddRadioWebDataProtection_AppliesTheRadioWebDiscriminator()
  {
    // Purpose isolation from the API's secrets ring: a payload protected by Radio.Web
    // must not be unprotectable by a provider using the Radio.Configuration
    // discriminator, even when both point at the same directory.
    var keysPath = Path.Combine(_testDirectory, "shared-ring");

    var webServices = new ServiceCollection();
    webServices.AddRadioWebDataProtection(keysPath);

    var apiLikeServices = new ServiceCollection();
    apiLikeServices.AddDataProtection()
      .SetApplicationName("Radio.Configuration")
      .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    using var webProvider = webServices.BuildServiceProvider();
    using var apiLikeProvider = apiLikeServices.BuildServiceProvider();

    var webPayload = webProvider.GetRequiredService<IDataProtectionProvider>()
      .CreateProtector("shared-purpose")
      .Protect("secret");

    var apiLikeProtector = apiLikeProvider.GetRequiredService<IDataProtectionProvider>()
      .CreateProtector("shared-purpose");

    Assert.ThrowsAny<Exception>(() => apiLikeProtector.Unprotect(webPayload));
    Assert.Equal("Radio.Web", DataProtectionSetup.ApplicationDiscriminator);
  }
}
