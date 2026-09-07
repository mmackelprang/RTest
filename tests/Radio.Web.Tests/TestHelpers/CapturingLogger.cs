using Microsoft.Extensions.Logging;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Records every entry as (level, formatted message) so a test can assert what was logged and
/// how many times. Lives in TestHelpers for the same reason <see cref="MockHttpHandler"/> does:
/// this assembly keeps one copy rather than a private nested one per test class.
/// <para>
/// <c>PhonePiiLogSafetyTests</c> keeps its own private copy on purpose — it also records
/// <c>exception?.ToString()</c>, which is load-bearing for that row and irrelevant here.
/// Consolidating the two is not GV-6's business.
/// </para>
/// </summary>
public sealed class CapturingLogger<T>(List<(LogLevel Level, string Message)> sink) : ILogger<T>
{
  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
    Exception? exception, Func<TState, Exception?, string> formatter) =>
    sink.Add((logLevel, formatter(state, exception)));
}
