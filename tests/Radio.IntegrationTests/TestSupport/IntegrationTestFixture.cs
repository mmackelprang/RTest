using Microsoft.Extensions.DependencyInjection;

namespace Radio.IntegrationTests.TestSupport;

/// <summary>
/// Base class for integration tests providing common setup and utilities.
/// </summary>
public abstract class IntegrationTestFixture : IAsyncLifetime
{
  private IntegrationTestWebApplicationFactory? _factory;
  private HttpClient? _client;

  /// <summary>
  /// Gets the test application factory.
  /// </summary>
  protected IntegrationTestWebApplicationFactory Factory =>
    _factory ?? throw new InvalidOperationException("Test not initialized");

  /// <summary>
  /// Gets the HTTP client for making requests to the test server.
  /// </summary>
  protected HttpClient Client =>
    _client ?? throw new InvalidOperationException("Test not initialized");

  /// <summary>
  /// Gets the temp directory for this test instance.
  /// </summary>
  protected string TempDirectory => Factory.TempDirectory;

  /// <summary>
  /// Override to enable background services. Default is false.
  /// </summary>
  protected virtual bool EnableBackgroundServices => false;

  /// <summary>
  /// Override to enable real audio engine. Default is false.
  /// </summary>
  protected virtual bool EnableRealAudioEngine => false;

  /// <summary>
  /// Override to disable mock fingerprinting. Default is true (mocks enabled).
  /// </summary>
  protected virtual bool UseMockFingerprinting => true;

  public virtual async Task InitializeAsync()
  {
    _factory = new IntegrationTestWebApplicationFactory
    {
      EnableBackgroundServices = EnableBackgroundServices,
      EnableRealAudioEngine = EnableRealAudioEngine,
      UseMockFingerprinting = UseMockFingerprinting,
      ConfigureTestServices = ConfigureServices
    };

    await _factory.InitializeAsync();
    _client = _factory.CreateClient();
  }

  public virtual async Task DisposeAsync()
  {
    _client?.Dispose();
    if (_factory != null)
    {
      await _factory.DisposeAsync();
    }
  }

  /// <summary>
  /// Override to add custom service registrations for the test.
  /// </summary>
  protected virtual void ConfigureServices(IServiceCollection services)
  {
  }

  /// <summary>
  /// Creates a new service scope for resolving scoped dependencies.
  /// </summary>
  protected IServiceScope CreateScope()
  {
    return Factory.CreateScope();
  }

  /// <summary>
  /// Gets a service from the root container.
  /// </summary>
  protected T? GetService<T>() where T : class
  {
    return Factory.GetService<T>();
  }

  /// <summary>
  /// Gets a required service from the root container.
  /// </summary>
  protected T GetRequiredService<T>() where T : notnull
  {
    return Factory.GetRequiredService<T>();
  }

  /// <summary>
  /// Gets a scoped service. Remember to dispose the scope when done.
  /// </summary>
  protected (T Service, IServiceScope Scope) GetScopedService<T>() where T : notnull
  {
    var scope = CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<T>();
    return (service, scope);
  }
}
