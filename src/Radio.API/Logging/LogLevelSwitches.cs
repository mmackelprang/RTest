using Microsoft.Extensions.Primitives;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Radio.API.Logging;

/// <summary>
/// LOG-5: runtime-adjustable Serilog minimum levels.
/// </summary>
/// <remarks>
/// <para>
/// Serilog fixes the <em>set</em> of level overrides when the logger is built, but each override (and
/// the default level) can be backed by a <see cref="LoggingLevelSwitch"/> whose level is read on every
/// event. This class builds one switch for <c>Serilog:MinimumLevel:Default</c> and one for every entry
/// under <c>Serilog:MinimumLevel:Override</c>, then installs them on the logger configuration so they
/// replace the fixed levels <c>ReadFrom.Configuration</c> installed for the same keys.
/// </para>
/// <para>
/// Consequences worth knowing before using it:
/// <list type="bullet">
///   <item>Only sources that are configured can be switched. A namespace with no override line in
///   configuration follows the most specific configured prefix, exactly as it does without this
///   class — so to make a namespace independently switchable, give it an override line.</item>
///   <item>A more specific override shadows a less specific one. Raising <c>Radio</c> to Debug does
///   not raise <c>Radio.Infrastructure.Audio</c> if that namespace has its own switch.</item>
///   <item>Editing a configured level while the process runs still takes effect, as it did before
///   this class existed (Serilog.Settings.Configuration subscribes to reloads). Unlike that
///   subscription, a switch is only touched when <em>its own</em> configured value changes — an
///   unrelated configuration write (the SQLite store reloads on every UI save) does not clobber a
///   runtime change.</item>
///   <item>Runtime changes are not persisted. A restart returns every switch to its configured
///   level, which is deliberate: a Debug level forgotten inside a sealed cabinet does not survive the
///   next restart.</item>
///   <item>Switches act on the logger pipeline, not on sinks. A sink with its own
///   <c>restrictedToMinimumLevel</c> (the API's Warning-only console sink, LOG-11) still applies, so
///   lowering a switch does not widen journald volume.</item>
///   <item>When configuration sets both the scalar <c>MinimumLevel</c> and <c>MinimumLevel:Default</c>,
///   this class takes <c>Default</c>; Serilog.Settings.Configuration picks whichever provider set one
///   last. Nothing here sets both.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class LogLevelSwitches
{
  /// <summary>The key under which the default (non-overridden) minimum level is exposed.</summary>
  public const string DefaultSource = "Default";

  // Serilog's own default when configuration names no minimum level.
  private const LogEventLevel SerilogDefaultLevel = LogEventLevel.Information;

  private readonly Dictionary<string, Entry> _entries;

  private LogLevelSwitches(Dictionary<string, Entry> entries)
  {
    _entries = entries;
  }

  /// <summary>
  /// Builds a switch per configured level. Unparseable values (for example a <c>$switchName</c>
  /// reference) are skipped — Default included — leaving whatever <c>ReadFrom.Configuration</c>
  /// installed for that key in place; they are not switchable through this class. Only when no default
  /// level is configured at all does Default get a switch at Serilog's own default, Information.
  /// </summary>
  public static LogLevelSwitches FromConfiguration(IConfiguration configuration)
  {
    var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    var minimumLevel = configuration.GetSection("Serilog:MinimumLevel");

    // Serilog.Settings.Configuration accepts both "MinimumLevel": "Debug" and
    // "MinimumLevel": { "Default": "Debug" }. Honour both.
    var defaultText = minimumLevel["Default"] ?? minimumLevel.Value;
    if (defaultText is null)
    {
      entries[DefaultSource] = new Entry(SerilogDefaultLevel);
    }
    else if (TryParseLevel(defaultText, out var parsedDefault))
    {
      entries[DefaultSource] = new Entry(parsedDefault);
    }

    foreach (var child in minimumLevel.GetSection("Override").GetChildren())
    {
      if (TryParseLevel(child.Value, out var level))
      {
        entries[child.Key] = new Entry(level);
      }
    }

    var switches = new LogLevelSwitches(entries);
    ChangeToken.OnChange(configuration.GetReloadToken, () => switches.OnConfigurationReloaded(configuration));
    return switches;
  }

  /// <summary>
  /// Installs the switches on <paramref name="loggerConfiguration"/>. Must be called <em>after</em>
  /// <c>ReadFrom.Configuration</c>: Serilog keeps one override per source and one default, and the
  /// last call for each wins, which is what lets these switches replace the fixed levels.
  /// </summary>
  public LoggerConfiguration ApplyTo(LoggerConfiguration loggerConfiguration)
  {
    if (_entries.TryGetValue(DefaultSource, out var defaultEntry))
    {
      loggerConfiguration.MinimumLevel.ControlledBy(defaultEntry.Switch);
    }
    foreach (var (source, entry) in _entries)
    {
      if (source == DefaultSource)
      {
        continue;
      }
      loggerConfiguration.MinimumLevel.Override(source, entry.Switch);
    }
    return loggerConfiguration;
  }

  /// <summary>Current and configured level of every switch, Default first then by source name.</summary>
  public IReadOnlyList<LogLevelState> GetAll()
  {
    return _entries
      .OrderBy(e => e.Key == DefaultSource ? 0 : 1)
      .ThenBy(e => e.Key, StringComparer.Ordinal)
      .Select(e => new LogLevelState(e.Key, e.Value.Switch.MinimumLevel, e.Value.ConfiguredLevel))
      .ToList();
  }

  /// <summary>Returns the state of one switch, or null if no switch exists for <paramref name="source"/>.</summary>
  public LogLevelState? Get(string source)
  {
    return _entries.TryGetValue(source, out var entry)
      ? new LogLevelState(source, entry.Switch.MinimumLevel, entry.ConfiguredLevel)
      : null;
  }

  /// <summary>
  /// Sets one switch. Returns the previous level, or null if no switch exists for
  /// <paramref name="source"/> (sources are matched exactly and case-sensitively, as Serilog matches
  /// them).
  /// </summary>
  public LogEventLevel? TrySet(string source, LogEventLevel level)
  {
    if (!_entries.TryGetValue(source, out var entry))
    {
      return null;
    }
    var previous = entry.Switch.MinimumLevel;
    entry.Switch.MinimumLevel = level;
    return previous;
  }

  /// <summary>Returns every switch to its configured level.</summary>
  public void ResetAll()
  {
    foreach (var entry in _entries.Values)
    {
      entry.Switch.MinimumLevel = entry.ConfiguredLevel;
    }
  }

  /// <summary>
  /// Applies a reloaded configuration: any switch whose <em>configured</em> level changed takes the
  /// new value (and it becomes what a reset restores). Switches whose configured value is unchanged
  /// keep whatever level they are running at. Sources added to configuration after startup are
  /// ignored — Serilog cannot add an override to a built logger.
  /// </summary>
  internal void OnConfigurationReloaded(IConfiguration configuration)
  {
    var minimumLevel = configuration.GetSection("Serilog:MinimumLevel");
    foreach (var (source, entry) in _entries)
    {
      var text = source == DefaultSource
        ? minimumLevel["Default"] ?? minimumLevel.Value
        : minimumLevel.GetSection("Override")[source];
      if (TryParseLevel(text, out var level) && level != entry.ConfiguredLevel)
      {
        entry.ConfiguredLevel = level;
        entry.Switch.MinimumLevel = level;
      }
    }
  }

  /// <summary>Parses a Serilog level name case-insensitively; rejects numeric strings.</summary>
  public static bool TryParseLevel(string? text, out LogEventLevel level)
  {
    level = default;
    // Enum.TryParse accepts "3" and "42", and ORs comma lists ("Debug,Information" is 1|2 = Warning);
    // a level is only ever meant to arrive as one name.
    if (string.IsNullOrWhiteSpace(text) || !char.IsLetter(text.Trim()[0]) || text.Contains(','))
    {
      return false;
    }
    return Enum.TryParse(text.Trim(), ignoreCase: true, out level) && Enum.IsDefined(level);
  }

  private sealed class Entry
  {
    public Entry(LogEventLevel configuredLevel)
    {
      ConfiguredLevel = configuredLevel;
      Switch = new LoggingLevelSwitch(configuredLevel);
    }

    // Written only by OnConfigurationReloaded; a torn read is impossible (an enum is one int).
    public LogEventLevel ConfiguredLevel { get; set; }

    public LoggingLevelSwitch Switch { get; }
  }
}

/// <summary>One switch's state as reported by the logging-level endpoint.</summary>
/// <param name="Source">The override source (a namespace prefix) or <c>Default</c>.</param>
/// <param name="Level">The level in force now.</param>
/// <param name="ConfiguredLevel">The level configuration set at startup; what a reset restores.</param>
public sealed record LogLevelState(string Source, LogEventLevel Level, LogEventLevel ConfiguredLevel);
