using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Radio.Web.Services.Hub;
using Xunit;

namespace Radio.Web.Tests.Services;

/// <summary>UI-10: the reconnect Warning, and the client-side premise the API's keepalive relies on.</summary>
public class HubReconnectLoggingTests
{
  [Fact]
  public void ClientDefaultServerTimeout_Is30Seconds()
  {
    // Radio.API's KeepAliveInterval (15 s) is sized against this default; none of radio-web's hub
    // services override it. If SignalR ever changes it, SignalRTimeoutTests' constant must follow.
    Assert.Equal(TimeSpan.FromSeconds(30), HubConnection.DefaultServerTimeout);
  }

  [Fact]
  public void ServerTimeout_IsAWarningWithTheMessage_AndNoStackTrace()
  {
    var log = new Capture();
    var timeout = new TimeoutException("Server timeout (30000.00ms) elapsed without receiving a message from the server.");

    HubReconnectLogging.LogReconnecting(log, timeout, "Visualization");

    var entry = Assert.Single(log.Entries);
    Assert.Equal(LogLevel.Warning, entry.Level);
    Assert.Null(entry.Exception);
    Assert.StartsWith("Visualization hub reconnecting: Server timeout (30000.00ms)", entry.Message);
  }

  [Fact]
  public void OtherFailures_KeepTheFullException()
  {
    var log = new Capture();
    var failure = new InvalidOperationException("boom");

    HubReconnectLogging.LogReconnecting(log, failure, "Audio");

    var entry = Assert.Single(log.Entries);
    Assert.Equal(LogLevel.Warning, entry.Level);
    Assert.Same(failure, entry.Exception);
    Assert.Equal("Audio hub reconnecting", entry.Message);
  }

  private sealed class Capture : ILogger
  {
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception), exception));
  }
}
