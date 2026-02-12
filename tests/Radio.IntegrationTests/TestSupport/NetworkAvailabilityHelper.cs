using System.Net.Sockets;

namespace Radio.IntegrationTests.TestSupport;

/// <summary>
/// Helper for integration tests that depend on external HTTP services.
/// Performs a lightweight TCP connectivity check and caches the result
/// so it's only done once per test run. Tests call RequireExternalNetwork()
/// at the top of [SkippableFact] methods to gracefully skip in CI/CD
/// environments without network access.
/// </summary>
public static class NetworkAvailabilityHelper
{
  private static bool? _isAvailable;
  private static readonly object Lock = new();

  /// <summary>
  /// Checks whether external network services are reachable.
  /// Result is cached for the lifetime of the test run.
  /// </summary>
  public static bool IsExternalNetworkAvailable
  {
    get
    {
      if (_isAvailable.HasValue)
        return _isAvailable.Value;

      lock (Lock)
      {
        if (_isAvailable.HasValue)
          return _isAvailable.Value;

        _isAvailable = CheckConnectivity();
        return _isAvailable.Value;
      }
    }
  }

  /// <summary>
  /// Call at the start of a [SkippableFact] test method to skip when
  /// external network services are unreachable (e.g., in CI/CD).
  /// </summary>
  public static void RequireExternalNetwork()
    => Skip.IfNot(IsExternalNetworkAvailable,
      "External network not available — skipping (CI/CD environment)");

  private static bool CheckConnectivity()
  {
    // Try a TCP connection to musicbrainz.org:443 with a short timeout.
    // This is lightweight (no TLS handshake, no HTTP overhead) and tests
    // real network connectivity to the services our integration tests use.
    try
    {
      using var client = new TcpClient();
      var connectTask = client.ConnectAsync("musicbrainz.org", 443);
      if (connectTask.Wait(TimeSpan.FromSeconds(3)))
        return true;
      return false;
    }
    catch
    {
      return false;
    }
  }
}
