using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.External;
using Radio.Infrastructure.Configuration.Abstractions;
using SpotifyAPI.Web;

namespace Radio.Infrastructure.External.Spotify;

/// <summary>
/// Implementation of Spotify OAuth authentication service using Authorization Code flow with PKCE.
/// Manages token lifecycle including generation, refresh, and persistence.
/// </summary>
public class SpotifyAuthService : ISpotifyAuthService
{
  private readonly ILogger<SpotifyAuthService> _logger;
  private readonly IOptionsMonitor<SpotifySecrets> _secrets;
  private readonly IConfigurationManager _configManager;
  
  // In-memory cache for current session
  private string? _currentAccessToken;
  private DateTimeOffset? _tokenExpiresAt;
  private string? _currentState;
  private string? _currentCodeVerifier;
  private readonly SemaphoreSlim _refreshLock = new(1, 1);

  /// <summary>
  /// Initializes a new instance of the <see cref="SpotifyAuthService"/> class.
  /// </summary>
  public SpotifyAuthService(
    ILogger<SpotifyAuthService> logger,
    IOptionsMonitor<SpotifySecrets> secrets,
    IConfigurationManager configManager)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
  }

  /// <inheritdoc/>
  public bool IsAuthenticated
  {
    get
    {
      if (_currentAccessToken == null || _tokenExpiresAt == null)
      {
        return false;
      }
      
      // Consider token expired if it expires in less than 5 minutes
      return _tokenExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(5);
    }
  }

  /// <inheritdoc/>
  public string? CurrentAccessToken
  {
    get
    {
      if (!IsAuthenticated)
      {
        return null;
      }
      
      return _currentAccessToken;
    }
  }

  /// <inheritdoc/>
  public Task<SpotifyAuthUrlResult> GenerateAuthorizationUrlAsync(
    string redirectUri,
    IEnumerable<string> scopes,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(redirectUri))
    {
      throw new ArgumentException("Redirect URI is required", nameof(redirectUri));
    }

    var secrets = _secrets.CurrentValue;
    if (string.IsNullOrWhiteSpace(secrets.ClientID))
    {
      throw new InvalidOperationException("Spotify Client ID is not configured");
    }

    // Generate PKCE parameters
    _currentCodeVerifier = GenerateCodeVerifier();
    var codeChallenge = GenerateCodeChallenge(_currentCodeVerifier);
    
    // Generate state for CSRF protection
    _currentState = GenerateState();

    // Build authorization URL
    var loginRequest = new LoginRequest(
      new Uri(redirectUri),
      secrets.ClientID,
      LoginRequest.ResponseType.Code)
    {
      CodeChallengeMethod = "S256",
      CodeChallenge = codeChallenge,
      State = _currentState,
      Scope = scopes.ToList()
    };

    var url = loginRequest.ToUri().ToString();
    
    _logger.LogInformation("Generated Spotify authorization URL with PKCE");

    return Task.FromResult(new SpotifyAuthUrlResult
    {
      Url = url,
      State = _currentState,
      CodeVerifier = _currentCodeVerifier
    });
  }

  /// <inheritdoc/>
  public async Task<SpotifyAuthTokenResult> HandleCallbackAsync(
    string code,
    string state,
    string codeVerifier,
    string redirectUri,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(code))
    {
      throw new ArgumentException("Authorization code is required", nameof(code));
    }

    if (string.IsNullOrWhiteSpace(state))
    {
      throw new ArgumentException("State parameter is required", nameof(state));
    }

    if (string.IsNullOrWhiteSpace(codeVerifier))
    {
      throw new ArgumentException("Code verifier is required", nameof(codeVerifier));
    }

    // Validate state to prevent CSRF attacks
    if (_currentState != null && state != _currentState)
    {
      _logger.LogWarning("State mismatch detected - possible CSRF attack");
      throw new InvalidOperationException("State parameter validation failed");
    }

    var secrets = _secrets.CurrentValue;
    if (string.IsNullOrWhiteSpace(secrets.ClientID) || string.IsNullOrWhiteSpace(secrets.ClientSecret))
    {
      throw new InvalidOperationException("Spotify credentials are not configured");
    }

    try
    {
      // Exchange authorization code for tokens
      var tokenRequest = new PKCETokenRequest(
        secrets.ClientID,
        code,
        new Uri(redirectUri),
        codeVerifier);

      var client = new OAuthClient();
      var response = await client.RequestToken(tokenRequest, cancellationToken);

      // Calculate expiration time
      var expiresAt = DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn);

      // Store tokens
      _currentAccessToken = response.AccessToken;
      _tokenExpiresAt = expiresAt;

      // Store refresh token in configuration
      if (!string.IsNullOrWhiteSpace(response.RefreshToken))
      {
        await SaveRefreshTokenAsync(response.RefreshToken, cancellationToken);
      }

      _logger.LogInformation("Successfully exchanged authorization code for tokens");

      return new SpotifyAuthTokenResult
      {
        AccessToken = response.AccessToken,
        RefreshToken = response.RefreshToken,
        ExpiresAt = expiresAt,
        TokenType = response.TokenType,
        Scopes = response.Scope?.Split(' ') ?? Array.Empty<string>()
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to exchange authorization code for tokens");
      throw new InvalidOperationException("Failed to complete OAuth authorization", ex);
    }
    finally
    {
      // Clear state and code verifier after use
      _currentState = null;
      _currentCodeVerifier = null;
    }
  }

  /// <inheritdoc/>
  public async Task<SpotifyAuthTokenResult> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
  {
    await _refreshLock.WaitAsync(cancellationToken);
    
    try
    {
      var secrets = _secrets.CurrentValue;
      
      if (string.IsNullOrWhiteSpace(secrets.RefreshToken))
      {
        throw new InvalidOperationException("No refresh token available. User must re-authenticate.");
      }

      if (string.IsNullOrWhiteSpace(secrets.ClientID) || string.IsNullOrWhiteSpace(secrets.ClientSecret))
      {
        throw new InvalidOperationException("Spotify credentials are not configured");
      }

      // Check if current token is still valid (with 1-minute buffer)
      if (_currentAccessToken != null && 
          _tokenExpiresAt != null && 
          _tokenExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(1))
      {
        _logger.LogDebug("Current access token is still valid, no refresh needed");
        return new SpotifyAuthTokenResult
        {
          AccessToken = _currentAccessToken,
          ExpiresAt = _tokenExpiresAt.Value
        };
      }

      try
      {
        _logger.LogInformation("Refreshing Spotify access token");

        // Use refresh token to get new access token
        var refreshRequest = new PKCETokenRefreshRequest(
          secrets.ClientID,
          secrets.RefreshToken);

        var client = new OAuthClient();
        var response = await client.RequestToken(refreshRequest, cancellationToken);

        // Calculate expiration time
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn);

        // Update cached tokens
        _currentAccessToken = response.AccessToken;
        _tokenExpiresAt = expiresAt;

        // If a new refresh token is provided, update it
        if (!string.IsNullOrWhiteSpace(response.RefreshToken))
        {
          await SaveRefreshTokenAsync(response.RefreshToken, cancellationToken);
        }

        _logger.LogInformation("Successfully refreshed access token");

        return new SpotifyAuthTokenResult
        {
          AccessToken = response.AccessToken,
          RefreshToken = response.RefreshToken,
          ExpiresAt = expiresAt,
          TokenType = response.TokenType,
          Scopes = response.Scope?.Split(' ') ?? Array.Empty<string>()
        };
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to refresh access token");
        
        // Clear cached tokens on refresh failure
        _currentAccessToken = null;
        _tokenExpiresAt = null;
        
        throw new InvalidOperationException("Failed to refresh access token. User may need to re-authenticate.", ex);
      }
    }
    finally
    {
      _refreshLock.Release();
    }
  }

  /// <inheritdoc/>
  public async Task<SpotifyAuthStatus> GetAuthenticationStatusAsync(CancellationToken cancellationToken = default)
  {
    if (!IsAuthenticated)
    {
      return new SpotifyAuthStatus
      {
        IsAuthenticated = false
      };
    }

    try
    {
      // Auto-refresh if needed
      if (_tokenExpiresAt != null && _tokenExpiresAt.Value < DateTimeOffset.UtcNow.AddMinutes(5))
      {
        await RefreshAccessTokenAsync(cancellationToken);
      }

      if (_currentAccessToken == null)
      {
        return new SpotifyAuthStatus
        {
          IsAuthenticated = false
        };
      }

      // Get user profile to verify authentication and get user info
      var config = SpotifyClientConfig.CreateDefault().WithToken(_currentAccessToken);
      var spotify = new SpotifyClient(config);
      var user = await spotify.UserProfile.Current(cancellationToken);

      return new SpotifyAuthStatus
      {
        IsAuthenticated = true,
        Username = user.Id,
        DisplayName = user.DisplayName,
        UserId = user.Uri,
        ExpiresAt = _tokenExpiresAt
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get authentication status");
      
      // Clear cached tokens on error
      _currentAccessToken = null;
      _tokenExpiresAt = null;
      
      return new SpotifyAuthStatus
      {
        IsAuthenticated = false
      };
    }
  }

  /// <inheritdoc/>
  public async Task LogoutAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogInformation("Logging out of Spotify");

    // Clear in-memory tokens
    _currentAccessToken = null;
    _tokenExpiresAt = null;
    _currentState = null;
    _currentCodeVerifier = null;

    // Clear refresh token from configuration
    try
    {
      await _configManager.SetValueAsync<string>(
        "audio-secrets",
        "Spotify:RefreshToken",
        "",
        cancellationToken);
        
      _logger.LogInformation("Successfully logged out of Spotify");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to clear refresh token from configuration");
      throw;
    }
  }

  /// <summary>
  /// Saves the refresh token to the configuration store.
  /// </summary>
  private async Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
  {
    try
    {
      await _configManager.SetValueAsync<string>(
        "audio-secrets",
        "Spotify:RefreshToken",
        refreshToken,
        cancellationToken);
        
      _logger.LogInformation("Refresh token saved to configuration");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to save refresh token to configuration");
      throw;
    }
  }

  /// <summary>
  /// Generates a cryptographically secure random code verifier for PKCE.
  /// </summary>
  private static string GenerateCodeVerifier()
  {
    const int length = 128;
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
    
    var bytes = new byte[length];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(bytes);
    
    var result = new char[length];
    for (int i = 0; i < length; i++)
    {
      result[i] = chars[bytes[i] % chars.Length];
    }
    
    return new string(result);
  }

  /// <summary>
  /// Generates the code challenge from the code verifier using SHA256.
  /// </summary>
  private static string GenerateCodeChallenge(string codeVerifier)
  {
    using var sha256 = SHA256.Create();
    var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
    return Convert.ToBase64String(challengeBytes)
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
  }

  /// <summary>
  /// Generates a random state parameter for CSRF protection.
  /// </summary>
  private static string GenerateState()
  {
    var bytes = new byte[32];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(bytes);
    return Convert.ToBase64String(bytes)
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
  }
}
