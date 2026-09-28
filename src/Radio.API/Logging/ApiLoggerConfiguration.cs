using Serilog;
using Serilog.Events;

namespace Radio.API.Logging;

/// <summary>
/// Assembles Radio.API's Serilog configuration. Lives outside <c>Program.cs</c> so the ordering it
/// depends on is covered by tests rather than by a comment.
/// </summary>
public static class ApiLoggerConfiguration
{
  /// <summary>
  /// Configuration-driven sinks and levels, then the LOG-5 runtime switches, then the systemd console
  /// sink.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠ <see cref="LogLevelSwitches.ApplyTo"/> must run after <c>ReadFrom.Configuration</c>. Both install
  /// a default level and one override per configured source, and Serilog keeps the last one for each;
  /// reversed, configuration's fixed levels win and every switch is inert.
  /// </para>
  /// <para>
  /// LOG-11: the console sink is restricted to Warning. Under systemd, stdout is captured by journald,
  /// so an unrestricted console sink meant every Information line was written twice — once to the
  /// journal and once to the file sink — on a box where log volume is an audio problem, not a disk
  /// problem. Dropping the sink outright was the other option in the row and is the wrong half: it
  /// would take the journald priority path with it, and <c>journalctl -p</c> is how this box gets
  /// triaged remotely. Warnings and errors still reach the journal; the file keeps full Information
  /// detail. Because the restriction is on the sink, lowering a LOG-5 switch never widens journald.
  /// </para>
  /// </remarks>
  public static LoggerConfiguration Build(IConfiguration configuration, LogLevelSwitches switches)
  {
    var loggerConfiguration = new LoggerConfiguration()
      .ReadFrom.Configuration(configuration);

    return switches.ApplyTo(loggerConfiguration)
      .WriteTo.Async(a => a.Console(
        new SystemdConsoleFormatter(),
        restrictedToMinimumLevel: LogEventLevel.Warning));
  }
}
