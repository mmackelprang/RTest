using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Infrastructure.DependencyInjection;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// GvMediaStartupCheck is the only runtime-observable behaviour this PR adds, and it had no test:
/// GvMediaRegistrationTests builds a provider but never starts hosted services, so nothing ran this
/// class at all.
///
/// <para>
/// It is also where the docs went wrong. GvMediaOptions.AuthKey used to claim this check "warns
/// about" a key mismatch; it does not, and the last test here is what makes that limitation a
/// pinned fact rather than a sentence someone can quietly re-break.
/// </para>
///
/// <para>
/// StartAsync is called directly rather than through a host. The class is an IHostedService only so
/// the container will run it — spinning a host to observe one log line would add startup, ordering
/// and teardown to a test about a two-branch <c>if</c>.
/// </para>
/// </summary>
public sealed class GvMediaStartupCheckTests
{
  private readonly List<(LogLevel Level, string Message)> _logs = [];

  private GvMediaStartupCheck CreateCheck(
    bool enabled, string authKey, string? webKey = null)
  {
    var settings = new Dictionary<string, string?>();
    if (webKey is not null)
    {
      settings["RotaryPhone:Gv:AuthKey"] = webKey;
    }

    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(settings)
      .Build();

    var options = new GvMediaOptions { Enabled = enabled, AuthKey = authKey };

    return new GvMediaStartupCheck(
      new ListLogger<GvMediaStartupCheck>(_logs),
      new StaticOptionsMonitor<GvMediaOptions>(options),
      configuration);
  }

  private async Task RunAsync(GvMediaStartupCheck check)
  {
    await check.StartAsync(CancellationToken.None);
    await check.StopAsync(CancellationToken.None);
  }

  [Fact]
  public async Task ADisabledGvMedia_SaysNothingAtBoot()
  {
    // The default state on every box today. A warning here would be noise on every restart, and
    // since LOG-11 warnings are exactly what does reach the journal.
    await RunAsync(CreateCheck(enabled: false, authKey: ""));

    Assert.Empty(_logs);
  }

  [Fact]
  public async Task ADisabledGvMedia_SaysNothingEvenWithAnEmptyKeyAndAWebKeyPresent()
  {
    // The Enabled gate is checked first and returns before either branch. Pinned because the
    // interesting state (empty key, web key visible) is precisely the one the second branch fires
    // on when Enabled is true.
    await RunAsync(CreateCheck(enabled: false, authKey: "", webKey: "shared-secret"));

    Assert.Empty(_logs);
  }

  [Fact]
  public async Task AnEmptyKeyWithNoWebKey_WarnsThatTheKeyIsUnset()
  {
    // The branch that normally fires on the appliance: Radio.API cannot see Radio.Web's overlay, so
    // "the other key is set" is invisible from here.
    await RunAsync(CreateCheck(enabled: true, authKey: ""));

    var warning = Assert.Single(_logs);
    Assert.Equal(LogLevel.Warning, warning.Level);
    Assert.Contains("GvMedia:AuthKey is empty", warning.Message, StringComparison.Ordinal);
    Assert.Contains("auth gate is off", warning.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task AnEmptyKeyWithTheWebKeyVisible_WarnsAboutTheDivergenceInstead()
  {
    // The narrow branch: it can only fire when RotaryPhone:Gv:AuthKey has also been placed in
    // Radio.API's own configuration or environment (RotaryPhone__Gv__AuthKey).
    await RunAsync(CreateCheck(enabled: true, authKey: "", webKey: "shared-secret"));

    var warning = Assert.Single(_logs);
    Assert.Equal(LogLevel.Warning, warning.Level);
    Assert.Contains("same secret under two keys", warning.Message, StringComparison.Ordinal);
    Assert.Contains("401", warning.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task WarningsNeverCarryTheKeyItself()
  {
    // Neither branch has any reason to print the secret, and both messages are Warning, which since
    // LOG-11 is the level that actually reaches the journal.
    await RunAsync(CreateCheck(enabled: true, authKey: "", webKey: "shared-secret"));

    Assert.All(
      _logs, l => Assert.DoesNotContain("shared-secret", l.Message, StringComparison.Ordinal));
  }

  [Fact]
  public async Task TwoDifferingNonEmptyKeys_ProduceNoWarningAtAll()
  {
    // The documented limitation, pinned. "A mismatch" means two non-empty keys that differ, and
    // that is the ONLY state producing the 401 on voicemail playback - yet the check is silent for
    // it, because its condition is "AuthKey is empty" and this key is not.
    //
    // It cannot be otherwise from inside Radio.API: /opt/radio-console/api/ and
    // /opt/radio-console/web/ hold separate appsettings.Production.json files, so the value
    // compared against here is not normally Radio.Web's at all. GvMediaFailure.Unauthorized is the
    // whole signal for this case, and this test exists so that stays true in the docs.
    await RunAsync(CreateCheck(enabled: true, authKey: "api-key", webKey: "web-key-that-differs"));

    Assert.Empty(_logs);
  }

  /// <summary>Records level and formatted message. Levels matter here; the masking pin's logger only keeps text.</summary>
  private sealed class ListLogger<T>(List<(LogLevel Level, string Message)> sink) : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) =>
      sink.Add((logLevel, formatter(state, exception)));
  }
}
