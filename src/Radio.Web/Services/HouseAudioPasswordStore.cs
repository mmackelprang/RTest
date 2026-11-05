using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// Keeps the RNET controller's password, encrypted with <c>Radio.Web</c>'s own key ring and stored in
/// the shared configuration store under <see cref="ConfigKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>A save is not readable through <see cref="IConfiguration"/> when it returns.</b> The write goes
/// through <c>radio-api</c>, in another process. This process re-reads the store only when the
/// <c>ConfigChanged</c> push arrives over <c>/hubs/audio</c>, which is sent as the POST finishes, and
/// which is simply lost if that hub is down. So <see cref="SetAsync"/> also keeps the protected value
/// in <see cref="_saved"/>, which <see cref="Get"/> prefers. Only the protected value is kept.
/// </para>
/// <para>
/// Singleton, for that field: every circuit, and the auth handler, which <c>IHttpClientFactory</c>
/// builds in a scope of its own, must see the same save.
/// </para>
/// </remarks>
public sealed class HouseAudioPasswordStore
{
  internal const string ConfigKey = "HouseAudio:ProtectedPassword";
  internal const string ProtectorPurpose = "HouseAudio.Password";

  private const string ConfigSection = "HouseAudio";
  private const string ConfigSectionKey = "ProtectedPassword";

  private readonly IConfiguration _configuration;
  private readonly IDataProtector _protector;
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<HouseAudioPasswordStore> _logger;

  private volatile string? _saved;
  private volatile bool _warnedUnreadable;

  public HouseAudioPasswordStore(
    IConfiguration configuration,
    IDataProtectionProvider dataProtection,
    IServiceScopeFactory scopeFactory,
    ILogger<HouseAudioPasswordStore> logger)
  {
    _configuration = configuration;
    _protector = dataProtection.CreateProtector(ProtectorPurpose);
    _scopeFactory = scopeFactory;
    _logger = logger;
  }

  /// <summary>
  /// The password, or null if none is stored or the key ring cannot decrypt it, in which case the UI
  /// asks for it again.
  /// </summary>
  public string? Get()
  {
    string? stored = _saved ?? _configuration[ConfigKey];
    if (string.IsNullOrEmpty(stored))
    {
      return null;
    }

    try
    {
      return _protector.Unprotect(stored);
    }
    catch (CryptographicException)
    {
      // This runs on every request while the tab is open, and the journal is not free on this box.
      if (!_warnedUnreadable)
      {
        _warnedUnreadable = true;
        _logger.LogWarning("The stored House Audio password cannot be decrypted; it must be entered again");
      }

      return null;
    }
  }

  /// <summary>
  /// Encrypts and saves the password. Returns false, keeping the previous one, if the password cannot
  /// travel in a header or the save fails. An unusable key ring is not handled here: encrypting then
  /// throws, as does every page render in this process.
  /// </summary>
  public async Task<bool> SetAsync(string password, CancellationToken ct)
  {
    if (!CanTravelInAHeader(password))
    {
      return false;
    }

    string protectedValue = _protector.Protect(password);

    // A scope per save, as GvBridgeStatusService does: a singleton must not hold a typed HttpClient.
    using IServiceScope scope = _scopeFactory.CreateScope();
    ConfigurationApiService configuration = scope.ServiceProvider.GetRequiredService<ConfigurationApiService>();
    if (!await configuration.UpdateConfigurationAsync(ConfigSection, ConfigSectionKey, protectedValue, ct))
    {
      return false;
    }

    _saved = protectedValue;
    _warnedUnreadable = false;
    return true;
  }

  // Header values are sent as ASCII here, and a line break or other control character cannot be sent.
  private static bool CanTravelInAHeader(string password) =>
    password.Length > 0 && password.All(c => c is >= ' ' and <= '~');
}
