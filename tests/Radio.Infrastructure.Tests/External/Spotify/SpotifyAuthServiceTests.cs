using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.External.Spotify;
using Xunit;

namespace Radio.Infrastructure.Tests.External.Spotify;

/// <summary>
/// Unit tests for SpotifyAuthService.
/// </summary>
public class SpotifyAuthServiceTests
{
  private readonly Mock<ILogger<SpotifyAuthService>> _loggerMock;
  private readonly Mock<IOptionsMonitor<SpotifySecrets>> _secretsMock;
  private readonly Mock<IConfigurationManager> _configManagerMock;
  private readonly SpotifySecrets _testSecrets;

  public SpotifyAuthServiceTests()
  {
    _loggerMock = new Mock<ILogger<SpotifyAuthService>>();
    _secretsMock = new Mock<IOptionsMonitor<SpotifySecrets>>();
    _configManagerMock = new Mock<IConfigurationManager>();

    _testSecrets = new SpotifySecrets
    {
      ClientID = "test-client-id",
      ClientSecret = "test-client-secret",
      RefreshToken = ""
    };

    _secretsMock.Setup(s => s.CurrentValue).Returns(_testSecrets);
  }

  [Fact]
  public void Constructor_WithNullLogger_ThrowsArgumentNullException()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() =>
      new SpotifyAuthService(null!, _secretsMock.Object, _configManagerMock.Object));
  }

  [Fact]
  public void Constructor_WithNullSecrets_ThrowsArgumentNullException()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() =>
      new SpotifyAuthService(_loggerMock.Object, null!, _configManagerMock.Object));
  }

  [Fact]
  public void Constructor_WithNullConfigManager_ThrowsArgumentNullException()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() =>
      new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, null!));
  }

  [Fact]
  public void IsAuthenticated_WhenNoToken_ReturnsFalse()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);

    // Act
    var result = service.IsAuthenticated;

    // Assert
    Assert.False(result);
  }

  [Fact]
  public void CurrentAccessToken_WhenNotAuthenticated_ReturnsNull()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);

    // Act
    var result = service.CurrentAccessToken;

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GenerateAuthorizationUrlAsync_WithValidParameters_ReturnsAuthUrl()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);
    var redirectUri = "http://localhost:5000/callback";
    var scopes = new[] { "user-read-private", "user-read-email" };

    // Act
    var result = await service.GenerateAuthorizationUrlAsync(redirectUri, scopes);

    // Assert
    Assert.NotNull(result);
    Assert.NotEmpty(result.Url);
    Assert.NotEmpty(result.State);
    Assert.NotEmpty(result.CodeVerifier);
    Assert.Contains("accounts.spotify.com/authorize", result.Url);
    Assert.Contains("client_id=" + _testSecrets.ClientID, result.Url);
    Assert.Contains("redirect_uri=", result.Url);
    Assert.Contains("code_challenge", result.Url);
    Assert.Contains("code_challenge_method=S256", result.Url);
  }

  [Fact]
  public async Task GenerateAuthorizationUrlAsync_WithEmptyRedirectUri_ThrowsArgumentException()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);
    var scopes = new[] { "user-read-private" };

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(async () =>
      await service.GenerateAuthorizationUrlAsync("", scopes));
  }

  [Fact]
  public async Task GenerateAuthorizationUrlAsync_WithoutClientID_ThrowsInvalidOperationException()
  {
    // Arrange
    var emptySecrets = new SpotifySecrets { ClientID = "", ClientSecret = "", RefreshToken = "" };
    _secretsMock.Setup(s => s.CurrentValue).Returns(emptySecrets);
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);
    var scopes = new[] { "user-read-private" };

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
      await service.GenerateAuthorizationUrlAsync("http://localhost/callback", scopes));
  }

  [Fact]
  public async Task HandleCallbackAsync_WithEmptyCode_ThrowsArgumentException()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(async () =>
      await service.HandleCallbackAsync("", "state", "verifier", "http://localhost/callback"));
  }

  [Fact]
  public async Task HandleCallbackAsync_WithEmptyState_ThrowsArgumentException()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(async () =>
      await service.HandleCallbackAsync("code", "", "verifier", "http://localhost/callback"));
  }

  [Fact]
  public async Task HandleCallbackAsync_WithEmptyCodeVerifier_ThrowsArgumentException()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(async () =>
      await service.HandleCallbackAsync("code", "state", "", "http://localhost/callback"));
  }

  [Fact]
  public async Task LogoutAsync_ClearsTokens()
  {
    // Arrange
    _configManagerMock
      .Setup(cm => cm.SetValueAsync<string>(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);

    // Act
    await service.LogoutAsync();

    // Assert
    Assert.False(service.IsAuthenticated);
    Assert.Null(service.CurrentAccessToken);
    _configManagerMock.Verify(cm => cm.SetValueAsync<string>(
      "audio-secrets",
      "Spotify:RefreshToken",
      "",
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task RefreshAccessTokenAsync_WithoutRefreshToken_ThrowsInvalidOperationException()
  {
    // Arrange
    var secretsWithoutToken = new SpotifySecrets
    {
      ClientID = "test-client-id",
      ClientSecret = "test-client-secret",
      RefreshToken = ""
    };
    _secretsMock.Setup(s => s.CurrentValue).Returns(secretsWithoutToken);
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
      await service.RefreshAccessTokenAsync());
  }

  [Fact]
  public async Task GetAuthenticationStatusAsync_WhenNotAuthenticated_ReturnsUnauthenticatedStatus()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);

    // Act
    var result = await service.GetAuthenticationStatusAsync();

    // Assert
    Assert.NotNull(result);
    Assert.False(result.IsAuthenticated);
    Assert.Null(result.Username);
    Assert.Null(result.DisplayName);
    Assert.Null(result.ExpiresAt);
  }

  [Fact]
  public async Task GenerateAuthorizationUrlAsync_GeneratesUniqueStateAndVerifier()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);
    var redirectUri = "http://localhost:5000/callback";
    var scopes = new[] { "user-read-private" };

    // Act
    var result1 = await service.GenerateAuthorizationUrlAsync(redirectUri, scopes);
    var result2 = await service.GenerateAuthorizationUrlAsync(redirectUri, scopes);

    // Assert
    Assert.NotEqual(result1.State, result2.State);
    Assert.NotEqual(result1.CodeVerifier, result2.CodeVerifier);
  }

  [Fact]
  public async Task GenerateAuthorizationUrlAsync_IncludesAllScopes()
  {
    // Arrange
    var service = new SpotifyAuthService(_loggerMock.Object, _secretsMock.Object, _configManagerMock.Object);
    var redirectUri = "http://localhost:5000/callback";
    var scopes = new[] { "user-read-private", "user-read-email", "playlist-read-private" };

    // Act
    var result = await service.GenerateAuthorizationUrlAsync(redirectUri, scopes);

    // Assert
    Assert.NotNull(result);
    Assert.Contains("scope=", result.Url);
    foreach (var scope in scopes)
    {
      Assert.Contains(scope, result.Url);
    }
  }
}
