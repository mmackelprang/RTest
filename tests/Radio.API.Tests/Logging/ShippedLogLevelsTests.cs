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
/// <para>
/// The O7 constraint is the point of these tests: the two namespaces are quiet by default, and the
/// Information lines <c>scripts/research/bt_drift_analyze.py</c> and <c>bt_stall_detect.py</c> parse
/// come back when both are raised at runtime (LOG-5). Those scripts read the file sink — radio-api's
/// journal has been Warning-only since LOG-11.
/// </para>
/// <para>
/// The SourceContexts are the loggers the lines are really written through. On Linux the BT
/// <c>BufferedSoundGenerator</c> is built by <c>LinuxBluetoothService.GetAudioCaptureDeviceAsync</c> with
/// that service's logger, so its drift/underrun lines — and <c>PipeWireNativeStream</c>'s OnProcess
/// heartbeat — are <c>...Platform.Bluetooth.LinuxBluetoothService</c>. The SDR generator logs as
/// <c>SDRRadioAudioSource</c>. <c>bt_stall_detect.py</c> also needs <c>BluetoothAudioSource</c>'s state
/// lines, which are in the Audio namespace.
/// </para>
/// </remarks>
public class ShippedLogLevelsTests : IDisposable
{
  private const string BtService = "Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService";
  private const string BtSource = "Radio.Infrastructure.Audio.Sources.Primary.BluetoothAudioSource";
  private const string SdrSource = "Radio.Infrastructure.Audio.Sources.Primary.SDRRadioAudioSource";

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
    // Carve-outs: source switching / announcements, and engine / device selection, stay visible.
    Assert.Equal(LogEventLevel.Information, switches.Get("Radio.Infrastructure.Audio.Services")!.Level);
    Assert.Equal(LogEventLevel.Information, switches.Get("Radio.Infrastructure.Audio.SoundFlow")!.Level);
    // LOG-12: SongRec's demoted lines come back with their own switch, not the whole of Radio.*.
    Assert.Equal(LogEventLevel.Information, switches.Get("Radio.Fingerprinting")!.Level);
  }

  [Fact]
  public void ParsedResearchLines_AreQuietByDefault_AndReturnWhenBothNamespacesAreRaised()
  {
    var config = Load(withDeployOverlay: true);
    var switches = LogLevelSwitches.FromConfiguration(config);
    var sink = new Collector();
    using var logger = ApiLoggerConfiguration.Build(config, switches).WriteTo.Sink(sink).CreateLogger();

    void EmitResearchLines()
    {
      logger.ForContext(Constants.SourceContextPropertyName, SdrSource)
        .Information("🔄 Clock drift compensation (Single): sdr drift");
      logger.ForContext(Constants.SourceContextPropertyName, BtService)
        .Information("🔄 Clock drift compensation (Single): bt drift");
      logger.ForContext(Constants.SourceContextPropertyName, BtService)
        .Information("🔬 PipeWire OnProcess: heartbeat");
      logger.ForContext(Constants.SourceContextPropertyName, BtSource)
        .Information("BluetoothAudioSource state = Playing");
    }

    EmitResearchLines();
    // Warnings in the same namespaces are untouched — "Buffer underrun" is a Warning.
    logger.ForContext(Constants.SourceContextPropertyName, BtService).Warning("⚠️ Buffer underrun (Single): underrun");
    // Not over-broad: the rest of Radio.*, and the carve-outs, keep Information.
    logger.ForContext(Constants.SourceContextPropertyName, "Radio.API.Services.AudioStateUpdateService").Information("api info");
    logger.ForContext(Constants.SourceContextPropertyName, "Radio.Infrastructure.Audio.Services.AudioManager").Information("switching source");
    logger.ForContext(Constants.SourceContextPropertyName, "Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager").Information("device selected");

    Assert.Equal(new[] { "⚠️ Buffer underrun (Single): underrun", "api info", "switching source", "device selected" }, sink.Take());

    // One namespace is not enough for bt_stall_detect: it needs the state line AND the heartbeat.
    switches.TrySet("Radio.Infrastructure.Platform.Bluetooth", LogEventLevel.Information);
    EmitResearchLines();
    Assert.Equal(new[] { "🔄 Clock drift compensation (Single): bt drift", "🔬 PipeWire OnProcess: heartbeat" }, sink.Take());

    switches.TrySet("Radio.Infrastructure.Audio", LogEventLevel.Information);
    EmitResearchLines();
    Assert.Equal(new[]
    {
      "🔄 Clock drift compensation (Single): sdr drift",
      "🔄 Clock drift compensation (Single): bt drift",
      "🔬 PipeWire OnProcess: heartbeat",
      "BluetoothAudioSource state = Playing",
    }, sink.Take());
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
