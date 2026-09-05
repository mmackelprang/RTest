using Microsoft.Extensions.Logging;

namespace Radio.API.Tests.TestSupport;

/// <summary>Captures every formatted log message, at every level, for the TTS-11 masking pins.</summary>
/// <remarks>
/// ⚠ <b>A DELIBERATE ~30-LINE DUPLICATE of the one in <c>Radio.Infrastructure.Tests</c></b>
/// (<c>External/GvMediaClientTests.cs</c>). That one is <c>internal</c>, so it is visible across
/// its own assembly and nowhere else; <c>Radio.API.Tests</c> is a separate assembly. Plan §4.1
/// weighed the two options and chose the copy: a new shared test package would be a build-graph
/// change carried by every test project in the solution, to save thirty lines.
///
/// <b><c>IsEnabled</c> returns <c>true</c> at EVERY level</b>, which is what makes Debug
/// sites observable. Several TTS-11 leaks are at Debug and therefore unwritten under today's
/// appsettings — they are one config edit from being written, so they are pinned anyway.
///
/// ⚠ <b>Synchronized, because the writers need not be the test thread.</b> <see cref="Messages"/>
/// hands back a SNAPSHOT so callers can enumerate it while a background path is still logging; a
/// bare list enumerated during an append throws, which would arrive as a rare unexplained failure
/// in a test that looks nothing like a concurrency test.
///
/// An exception is captured as a SECOND entry via <c>ToString()</c>: user text embedded in an
/// exception message reaches the sink exactly as a log argument would, so it must be asserted on.
/// </remarks>
internal class CapturingLoggerProvider
{
  private readonly List<string> _messages = [];

  /// <summary>A point-in-time copy. Safe to enumerate while background logging continues.</summary>
  public IReadOnlyList<string> Messages
  {
    get { lock (_messages) { return _messages.ToArray(); } }
  }

  public ILogger<T> CreateLogger<T>() => new CapturingLogger<T>(_messages);

  private sealed class CapturingLogger<T>(List<string> sink) : ILogger<T>
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      lock (sink)
      {
        sink.Add(formatter(state, exception));
        if (exception is not null)
        {
          sink.Add(exception.ToString());
        }
      }
    }
  }
}
