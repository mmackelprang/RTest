namespace Radio.Web.Configuration;

using Microsoft.AspNetCore.DataProtection;

/// <summary>
/// Wires up the ASP.NET Core DataProtection key ring for Radio.Web.
/// </summary>
/// <remarks>
/// Radio.Web stores no secrets of its own, but it still needs a writable key ring:
/// Blazor Server protects the serialized marker it emits for each interactive root
/// component — one per render-mode boundary (<c>SSRRenderModeBoundary.ToMarker</c> →
/// <c>ServerComponentSerializer.CreateSerializedServerComponent</c> →
/// <c>IDataProtector.Protect</c>). That marker is emitted for
/// <c>InteractiveServerRenderMode(prerender: false)</c> too — suppressing
/// prerendering does not suppress the marker — so a key ring the process cannot
/// write makes every page render throw, not just pages carrying protected payloads.
/// This is what took the kiosk UI down on 2026-08-16; see
/// design/plans/SECRET-KEYRING-INVESTIGATION.md.
/// </remarks>
internal static class DataProtectionSetup
{
  /// <summary>
  /// Purpose discriminator applied to every payload Radio.Web protects.
  /// </summary>
  /// <remarks>
  /// Deliberately different from the <c>"Radio.Configuration"</c> discriminator that
  /// <c>AddManagedConfiguration</c> sets for the API's secrets store: Radio.Web only
  /// protects short-lived UI payloads and has no reason to be able to unprotect API
  /// secrets. Changing this string invalidates payloads already in flight (open
  /// circuits, outstanding antiforgery tokens); it does not change or invalidate the
  /// keys themselves.
  /// </remarks>
  internal const string ApplicationDiscriminator = "Radio.Web";

  /// <summary>
  /// Directory name, under the data root, used when no explicit
  /// <c>DataProtection:KeysPath</c> is configured.
  /// </summary>
  /// <remarks>
  /// Intentionally not <c>keys</c>: that is the API's ring (see
  /// <c>src/Radio.API/appsettings.json</c>), which encrypts the stored secrets.
  /// Keeping Radio.Web's keys in a separate directory means the secrets ring only
  /// ever contains keys the API created, so a future secrets investigation does not
  /// have to attribute unfamiliar key files. It is not a security boundary — both
  /// services run as the same user and either could read the other's directory.
  /// </remarks>
  private const string DefaultKeysDirectoryName = "keys-web";

  /// <summary>
  /// Resolves the absolute key-ring directory for Radio.Web.
  /// </summary>
  /// <param name="configuration">Configuration to read the path settings from.</param>
  /// <param name="baseDirectory">
  /// Absolute directory that a relative configured path is resolved against.
  /// Ignored when the configured path is already rooted.
  /// </param>
  /// <returns>An absolute path. This method does not create the directory.</returns>
  internal static string ResolveKeysPath(IConfiguration configuration, string baseDirectory)
  {
    // Resolution order mirrors Radio.Configuration's AddManagedConfiguration:
    //   1. DataProtection:KeysPath (explicit override), else
    //   2. <Database:RootPath>/keys-web, else
    //   3. ./data/keys-web
    // This method never reads HOME: the result depends only on the configured
    // values and the caller-supplied base directory, so a change to the process's
    // home directory (or a sandbox that masks it) cannot move the ring.
    var keysPath = configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(keysPath))
    {
      var dataRoot = configuration["Database:RootPath"];
      if (string.IsNullOrWhiteSpace(dataRoot))
      {
        dataRoot = "./data";
      }

      keysPath = Path.Combine(dataRoot, DefaultKeysDirectoryName);
    }

    return Path.GetFullPath(keysPath, baseDirectory);
  }

  /// <summary>
  /// Registers DataProtection with the key ring persisted to <paramref name="keysPath"/>.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="keysPath">
  /// Absolute key-ring directory, as returned by <see cref="ResolveKeysPath"/>.
  /// </param>
  /// <returns>The service collection for chaining.</returns>
  internal static IServiceCollection AddRadioWebDataProtection(
    this IServiceCollection services,
    string keysPath)
  {
    // Create the ring directory up front so a clean box works on first run.
    // This is NOT a writability check: CreateDirectory returns successfully when
    // the directory already exists, including on a read-only mount. It throws only
    // when the directory is missing and cannot be created.
    Directory.CreateDirectory(keysPath);

    services.AddDataProtection()
      .SetApplicationName(ApplicationDiscriminator)
      .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    return services;
  }
}
