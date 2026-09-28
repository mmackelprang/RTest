using Microsoft.Extensions.Configuration;
using Radio.API.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Radio.API.Tests.Logging;

/// <summary>
/// LOG-5: the runtime switches must change what the <em>real</em> API logger emits, without a rebuild.
/// </summary>
/// <remarks>
/// Every test drives <see cref="ApiLoggerConfiguration.Build"/> — the same method Program.cs calls —
/// so a regression in the order that makes the switches win (ApplyTo after ReadFrom.Configuration)
/// fails here, not only in a hand-built logger. Events are collected by an in-memory sink added after
/// Build; configuration declares no sinks, so only the console sink (Warning+) and the collector exist.
/// </remarks>
public class LogLevelSwitchesTests
{
  private static IConfigurationRoot Config(Dictionary<string, string?> values)
  {
    return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
  }

  private static readonly Dictionary<string, string?> ProductionShape = new()
  {
    ["Serilog:MinimumLevel:Default"] = "Warning",
    ["Serilog:MinimumLevel:Override:Radio"] = "Information",
    ["Serilog:MinimumLevel:Override:Radio.Infrastructure.Audio"] = "Warning",
  };

  private static (Logger Logger, CollectingSink Sink, LogLevelSwitches Switches) Build(IConfiguration config)
  {
    var switches = LogLevelSwitches.FromConfiguration(config);
    var sink = new CollectingSink();
    var logger = ApiLoggerConfiguration.Build(config, switches).WriteTo.Sink(sink).CreateLogger();
    return (logger, sink, switches);
  }

  [Fact]
  public void FromConfiguration_ExposesDefaultAndEveryOverride_AtConfiguredLevels()
  {
    var switches = LogLevelSwitches.FromConfiguration(Config(ProductionShape));

    var all = switches.GetAll();
    Assert.Equal(new[] { "Default", "Radio", "Radio.Infrastructure.Audio" }, all.Select(s => s.Source));
    Assert.Equal(LogEventLevel.Warning, all[0].Level);
    Assert.Equal(LogEventLevel.Information, all[1].Level);
    Assert.All(all, s => Assert.Equal(s.ConfiguredLevel, s.Level));
  }

  [Fact]
  public void ConfiguredLevels_AreHonouredBeforeAnyRuntimeChange()
  {
    var (logger, sink, _) = Build(Config(ProductionShape));

    logger.ForContext(Constants.SourceContextPropertyName, "Radio.Infrastructure.Audio.Mixer").Information("audio info");
    logger.ForContext(Constants.SourceContextPropertyName, "Radio.API.Controllers.X").Information("api info");
    logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.Foo").Information("ms info");

    Assert.Equal(new[] { "api info" }, sink.Messages);
  }

  [Fact]
  public void TrySet_OnOverride_ChangesEmissionForThatNamespace_WithoutRebuildingTheLogger()
  {
    var (logger, sink, switches) = Build(Config(ProductionShape));
    var audio = logger.ForContext(Constants.SourceContextPropertyName, "Radio.Infrastructure.Audio.Mixer");

    audio.Information("before");
    Assert.Equal(LogEventLevel.Warning, switches.TrySet("Radio.Infrastructure.Audio", LogEventLevel.Debug));
    audio.Debug("during");
    switches.ResetAll();
    audio.Information("after");

    Assert.Equal(new[] { "during" }, sink.Messages);
  }

  [Fact]
  public void TrySet_OnDefault_ChangesEmissionForUnoverriddenSources()
  {
    var (logger, sink, switches) = Build(Config(ProductionShape));
    var ms = logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.Foo");

    ms.Information("before");
    switches.TrySet(LogLevelSwitches.DefaultSource, LogEventLevel.Information);
    ms.Information("during");

    Assert.Equal(new[] { "during" }, sink.Messages);
  }

  [Fact]
  public void TrySet_OnPrefix_DoesNotReachAMoreSpecificOverride()
  {
    // The documented shadowing rule: a namespace with its own switch follows only that switch.
    var (logger, sink, switches) = Build(Config(ProductionShape));

    switches.TrySet("Radio", LogEventLevel.Debug);
    logger.ForContext(Constants.SourceContextPropertyName, "Radio.Infrastructure.Audio.Mixer").Information("audio info");
    logger.ForContext(Constants.SourceContextPropertyName, "Radio.API.X").Debug("api debug");

    Assert.Equal(new[] { "api debug" }, sink.Messages);
  }

  [Fact]
  public void TrySet_UnknownSource_ReturnsNullAndChangesNothing()
  {
    var switches = LogLevelSwitches.FromConfiguration(Config(ProductionShape));

    Assert.Null(switches.TrySet("Radio.Nope", LogEventLevel.Debug));
    // Case-sensitive, as Serilog's own override matching is.
    Assert.Null(switches.TrySet("radio", LogEventLevel.Debug));
    Assert.All(switches.GetAll(), s => Assert.Equal(s.ConfiguredLevel, s.Level));
  }

  [Fact]
  public void ConsoleSink_StaysWarningOnly_WhenASwitchIsLowered()
  {
    // LOG-11 guarantee survives LOG-5: lowering a switch must not widen journald. The console sink
    // is the journald path under systemd; capture it and emit an Information line with every switch
    // at Verbose.
    var original = Console.Out;
    var captured = new StringWriter();
    Console.SetOut(captured);
    try
    {
      var config = Config(ProductionShape);
      var switches = LogLevelSwitches.FromConfiguration(config);
      using (var logger = ApiLoggerConfiguration.Build(config, switches).CreateLogger())
      {
        foreach (var s in switches.GetAll())
        {
          switches.TrySet(s.Source, LogEventLevel.Verbose);
        }
        logger.ForContext(Constants.SourceContextPropertyName, "Radio.X").Information("info-line-LOG5");
        logger.ForContext(Constants.SourceContextPropertyName, "Radio.X").Warning("warn-line-LOG5");
      } // dispose flushes the async console wrapper

      var text = captured.ToString();
      Assert.DoesNotContain("info-line-LOG5", text);
      Assert.Contains("warn-line-LOG5", text);
    }
    finally
    {
      Console.SetOut(original);
    }
  }

  [Fact]
  public void ConfigurationReload_WithUnchangedLevels_DoesNotClobberARuntimeChange()
  {
    // The SQLite config store reloads on every UI save. Serilog.Settings.Configuration's own
    // subscription would reset every switch to configuration on each of those; ours must not.
    var config = Config(ProductionShape);
    var switches = LogLevelSwitches.FromConfiguration(config);
    switches.TrySet("Radio", LogEventLevel.Debug);

    config.Reload();

    Assert.Equal(LogEventLevel.Debug, switches.Get("Radio")!.Level);
  }

  [Fact]
  public void ConfigurationReload_WithAChangedLevel_AppliesItAndMakesItTheResetTarget()
  {
    var values = new Dictionary<string, string?>(ProductionShape);
    var provider = new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = values };
    var config = new ConfigurationBuilder().Add(provider).Build();
    var switches = LogLevelSwitches.FromConfiguration(config);

    config["Serilog:MinimumLevel:Override:Radio"] = "Error";
    config.Reload();

    Assert.Equal(LogEventLevel.Error, switches.Get("Radio")!.Level);
    switches.TrySet("Radio", LogEventLevel.Debug);
    switches.ResetAll();
    Assert.Equal(LogEventLevel.Error, switches.Get("Radio")!.Level);
  }

  [Fact]
  public void FromConfiguration_WithScalarMinimumLevel_UsesItAsDefault()
  {
    var switches = LogLevelSwitches.FromConfiguration(Config(new() { ["Serilog:MinimumLevel"] = "Debug" }));
    Assert.Equal(LogEventLevel.Debug, switches.Get("Default")!.Level);
  }

  [Fact]
  public void FromConfiguration_WithNoMinimumLevel_DefaultsToInformation()
  {
    var switches = LogLevelSwitches.FromConfiguration(Config(new()));
    Assert.Equal(LogEventLevel.Information, switches.Get("Default")!.Level);
  }

  [Theory]
  [InlineData("debug", true)]
  [InlineData(" Information ", true)]
  [InlineData("3", false)]
  [InlineData("42", false)]
  [InlineData("Loud", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  public void TryParseLevel_AcceptsNamesOnly(string? text, bool expected)
  {
    Assert.Equal(expected, LogLevelSwitches.TryParseLevel(text, out _));
  }

  private sealed class CollectingSink : ILogEventSink
  {
    private readonly List<string> _messages = new();

    public IReadOnlyList<string> Messages
    {
      get
      {
        lock (_messages)
        {
          return _messages.ToList();
        }
      }
    }

    public void Emit(LogEvent logEvent)
    {
      lock (_messages)
      {
        _messages.Add(logEvent.MessageTemplate.Text);
      }
    }
  }
}
