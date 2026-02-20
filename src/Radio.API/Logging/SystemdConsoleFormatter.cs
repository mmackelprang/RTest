using Serilog.Events;
using Serilog.Formatting;

namespace Radio.API.Logging;

/// <summary>
/// Formats Serilog log events with systemd syslog-level prefixes (&lt;N&gt;).
/// When <c>SyslogLevelPrefix=true</c> is set in the systemd service file,
/// journald uses these prefixes to assign proper priority levels.
/// Lines WITHOUT a prefix (e.g., ALSA/JACK C library noise written directly
/// to stdout) default to the <c>LogLevelMax</c> setting, allowing them to
/// be filtered out with <c>journalctl -p info</c>.
/// </summary>
public sealed class SystemdConsoleFormatter : ITextFormatter
{
  public void Format(LogEvent logEvent, TextWriter output)
  {
    // Syslog priority: 7=debug, 6=info, 5=notice, 4=warning, 3=error, 2=critical
    var priority = logEvent.Level switch
    {
      LogEventLevel.Verbose => 7,
      LogEventLevel.Debug => 7,
      LogEventLevel.Information => 6,
      LogEventLevel.Warning => 4,
      LogEventLevel.Error => 3,
      LogEventLevel.Fatal => 2,
      _ => 6
    };

    output.Write($"<{priority}>");
    output.Write($"[{logEvent.Timestamp:HH:mm:ss} {LevelAbbreviation(logEvent.Level)}] ");
    logEvent.RenderMessage(output);
    output.WriteLine();

    if (logEvent.Exception != null)
    {
      output.Write($"<{priority}>");
      output.WriteLine(logEvent.Exception.ToString());
    }
  }

  private static string LevelAbbreviation(LogEventLevel level) => level switch
  {
    LogEventLevel.Verbose => "VRB",
    LogEventLevel.Debug => "DBG",
    LogEventLevel.Information => "INF",
    LogEventLevel.Warning => "WRN",
    LogEventLevel.Error => "ERR",
    LogEventLevel.Fatal => "FTL",
    _ => "???"
  };
}
