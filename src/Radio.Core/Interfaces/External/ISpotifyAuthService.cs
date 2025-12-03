namespace Radio.Core.Interfaces.External;

/// <summary>
/// Service for managing Spotify OAuth authentication flow.
/// Implements Authorization Code flow with PKCE for secure token management.
/// </summary>
public interface ISpotifyAuthService
{
  /// <summary>
  /// Generates the Spotify authorization URL for the user to grant permissions.
  /// Uses PKCE (Proof Key for Code Exchange) for enhanced security.
  /// </summary>
  /// <param name="redirectUri">The redirect URI registered with Spotify app.</param>
  /// <param name="scopes">The requested Spotify API scopes.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>An object containing the authorization URL and state parameter.</returns>
  Task<SpotifyAuthUrlResult> GenerateAuthorizationUrlAsync(
    string redirectUri,
    IEnumerable<string> scopes,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Handles the OAuth callback by exchanging the authorization code for access and refresh tokens.
  /// Stores the refresh token in SpotifySecrets configuration for persistence.
  /// </summary>
  /// <param name="code">The authorization code from Spotify callback.</param>
  /// <param name="state">The state parameter to validate against CSRF attacks.</param>
  /// <param name="codeVerifier">The PKCE code verifier that matches the challenge sent in authorization.</param>
  /// <param name="redirectUri">The same redirect URI used in the authorization request.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>An object containing the access token, refresh token, and expiration time.</returns>
  Task<SpotifyAuthTokenResult> HandleCallbackAsync(
    string code,
    string state,
    string codeVerifier,
    string redirectUri,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Refreshes the access token using the stored refresh token.
  /// Called automatically when the access token expires.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>An object containing the new access token and expiration time.</returns>
  Task<SpotifyAuthTokenResult> RefreshAccessTokenAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the current authentication status.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>An object containing authentication status and user information.</returns>
  Task<SpotifyAuthStatus> GetAuthenticationStatusAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Clears all stored tokens and authentication state.
  /// Effectively logs the user out of Spotify.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  Task LogoutAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Checks if a valid access token is available (not expired).
  /// </summary>
  bool IsAuthenticated { get; }

  /// <summary>
  /// Gets the current access token if available and not expired.
  /// Returns null if not authenticated or token is expired.
  /// </summary>
  string? CurrentAccessToken { get; }
}

/// <summary>
/// Result of generating a Spotify authorization URL.
/// </summary>
public record SpotifyAuthUrlResult
{
  /// <summary>
  /// The authorization URL to redirect the user to.
  /// </summary>
  public required string Url { get; init; }

  /// <summary>
  /// The state parameter used for CSRF protection.
  /// Should be stored and validated when the callback is received.
  /// </summary>
  public required string State { get; init; }

  /// <summary>
  /// The PKCE code verifier.
  /// Should be stored and used when exchanging the authorization code for tokens.
  /// </summary>
  public required string CodeVerifier { get; init; }
}

/// <summary>
/// Result of token operations (callback or refresh).
/// </summary>
public record SpotifyAuthTokenResult
{
  /// <summary>
  /// The access token for making Spotify API requests.
  /// </summary>
  public required string AccessToken { get; init; }

  /// <summary>
  /// The refresh token for obtaining new access tokens.
  /// Only present in the initial authorization callback.
  /// </summary>
  public string? RefreshToken { get; init; }

  /// <summary>
  /// When the access token expires.
  /// </summary>
  public required DateTimeOffset ExpiresAt { get; init; }

  /// <summary>
  /// The token type (typically "Bearer").
  /// </summary>
  public string TokenType { get; init; } = "Bearer";

  /// <summary>
  /// The granted scopes.
  /// </summary>
  public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Authentication status information.
/// </summary>
public record SpotifyAuthStatus
{
  /// <summary>
  /// Whether the user is currently authenticated.
  /// </summary>
  public required bool IsAuthenticated { get; init; }

  /// <summary>
  /// The Spotify username if authenticated.
  /// </summary>
  public string? Username { get; init; }

  /// <summary>
  /// The user's display name if authenticated.
  /// </summary>
  public string? DisplayName { get; init; }

  /// <summary>
  /// When the current access token expires (if authenticated).
  /// </summary>
  public DateTimeOffset? ExpiresAt { get; init; }

  /// <summary>
  /// The user's Spotify user ID.
  /// </summary>
  public string? UserId { get; init; }
}
