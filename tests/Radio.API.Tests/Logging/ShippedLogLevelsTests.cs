using Microsoft.Extensions.Configuration;
using Radio.API.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Radio.API.Tests.Logging;

/// <summary>
/// LOG-2 against the files that actually ship: <c>src/Radio.API/appsettings.json</c>, alone and layered
/// with the Debian deploy overlay the way the installed box loads them.
/// </summary>
/// <remarks>
/// The O7 constraint is the point of these tests: the two namespaces are quiet by default, and their
/// Information lines — the ones <c>scripts/research/bt_drift_analyze.py</c> and
/// <c>bt_stall_detect.py</c> parse — come back with one LOG-5 switch each. The SourceContexts below
/// are the loggers those lines are actually written through.
/// </remarks>
public class ShippedLogLevelsTests : IDisposable
{
  // BufferedSoundGenerator lines are written through the owning source's logger.
  private const string BtSource = "Radio.Infrastructure.Audio.Sources.Primary.BluetoothAudioSource";
  // PipeWireNativeStream's OnProcess line is written through LinuxBluetoothService's logger.
  private const string BtService = "Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService";

  private readonly string _logDir = Path.Combine(Path.GetTempPath(), $"radio-log2-{Guid.NewGuid():N}");

  public void Dispose()
  {
    try { Directory.Delete(_logDir, recursive: true); } catch { /* best effort */ }
  }

  private static string RepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RadioConsole.sln")))
    {
      dir = dir.Parent;
    }
    return dir?.FullName ?? throw new InvalidOperationException("RadioConsole.sln not found above the test binaries");
  }

  private IConfigurationRoot Load(bool withDeployOverlay)
  {
    var root = RepoRoot();
    var builder = new ConfigurationBuilder()
      .AddJsonFile(Path.Combine(root, "src", "Radio.API", "appsettings.json"), optional: false);
    if (withDeployOverlay)
    {
      builder.AddJsonFile(Path.Combine(root, "deploy", "debian-x64", "appsettings.Production.json"), optional: false);
    }
    // Keep the real file sink out of the working directory.
    builder.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Serilog:WriteTo:0:Args:configure:0:Args:path"] = Path.Combine(_logDir, "radio-.txt"),
    });
    return builder.Build();
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void AudioAndBluetooth_AreSwitchesAtWarning(bool withDeployOverlay)
  {
    var switches = LogLevelSwitches.FromConfiguration(Load(withDeployOverlay));

    Assert.Equal(LogEventLevel.Warning, switches.Get("Radio.Infrastructure.Audio")!.Level);
    Assert.Equal(LogEventLevel.Warning, switches.Get("Radio.Infrastructure.Platform.Bluetooth")!.Level);
    Assert.Equal(LogEventLevel.Information, switches.Get("Radio")!.Level);
  }

  [Fact]
  public void ParsedResearchLines_AreQuietByDefault_AndReturnWithOneSwitchEach()
  {
    var config = Load(withDeployOverlay: true);
    var switches = LogLevelSwitches.FromConfiguration(config);
    var sink = new Collector();
    using var logger = ApiLoggerConfiguration.Build(config, switches).WriteTo.Sink(sink).CreateLogger();

    void EmitResearchLines()
    {
      logger.ForContext(Constants.SourceContextPropertyName, BtSource)
        .Information("🔄 Clock drift compensation (Single): drift");
      logger.ForContext(Constants.SourceContextPropertyName, BtService)
        .Information("🔬 PipeWire OnProcess: heartbeat");
    }

    EmitResearchLines();
    // Warnings in the same namespaces are untouched — "Buffer underrun" is a Warning.
    logger.ForContext(Constants.SourceContextPropertyName, BtSource).Warning("⚠️ Buffer underrun (Single): underrun");
    // The override is not over-broad: the rest of Radio.* keeps Information.
    logger.ForContext(Constants.SourceContextPropertyName, "Radio.API.Services.AudioStateUpdateService").Information("api info");
    logger.ForContext(Constants.SourceContextPropertyName, "Radio.Fingerprinting.Services.X").Information("fp info");

    Assert.Equal(new[] { "⚠️ Buffer underrun (Single): underrun", "api info", "fp info" }, sink.Take());

    switches.TrySet("Radio.Infrastructure.Audio", LogEventLevel.Information);
    switches.TrySet("Radio.Infrastructure.Platform.Bluetooth", LogEventLevel.Information);
    EmitResearchLines();

    Assert.Equal(new[] { "🔄 Clock drift compensation (Single): drift", "🔬 PipeWire OnProcess: heartbeat" }, sink.Take());
  }

  private sealed class Collector : ILogEventSink
  {
    private readonly List<string> _messages = new();

    public void Emit(LogEvent logEvent)
    {
      lock (_messages) { _messages.Add(logEvent.MessageTemplate.Text); }
    }

    public string[] Take()
    {
      lock (_messages)
      {
        var copy = _messages.ToArray();
        _messages.Clear();
        return copy;
      }
    }
  }
}
