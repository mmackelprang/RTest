using Radio.API.Hubs;
using Radio.API.Logging;
using Radio.API.Middleware;
using Radio.API.Services;
using Radio.API.Streaming;
using Radio.Core.Constants;
using Radio.Core.Interfaces;
using Radio.Configuration.Bridge;
using Radio.Infrastructure.DependencyInjection;
using Radio.Metrics;
using Scalar.AspNetCore;
using Serilog;


var builder = WebApplication.CreateBuilder(args);

// Add custom configuration source (config.json) which is managed by ConfigurationManager
// This ensures that persistent settings saved by the app are loaded and reloaded on change
builder.Configuration.AddJsonFile("config.json", optional: true, reloadOnChange: true);

// Bridge the SQLite config store into .NET's IConfiguration pipeline.
// Values written by the UI (via ConfigurationManager → SQLite) now override appsettings.json
// defaults, and ConfigStoreChangeNotifier triggers IOptionsMonitor re-evaluation on writes.
var dbSection = builder.Configuration.GetSection("Database");
var rootPath = dbSection["RootPath"] ?? "./data";
var configSubdir = dbSection["ConfigurationSubdirectory"] ?? "config";
var configFile = dbSection["ConfigurationFileName"] ?? "configuration.db";
var configDbPath = Path.GetFullPath(Path.Combine(rootPath, configSubdir, configFile));

var configStoreNotifier = new ConfigStoreChangeNotifier();
builder.Configuration.AddSqliteConfigStore(configDbPath, "sqlite", configStoreNotifier);
builder.Services.AddSingleton(configStoreNotifier);

// Configure Serilog with systemd-compatible console formatter.
// The SystemdConsoleFormatter prefixes each log line with <N> syslog priority
// (e.g., <6> for info, <4> for warning). When SyslogLevelPrefix=true is set
// in the systemd service file, journald assigns proper priority levels.
// ALSA/JACK C library noise (written directly to stdout without a prefix)
// gets the default priority, allowing filtering with `journalctl -p info`.
//
// LOG-11: the console sink is restricted to Warning — see ApiLoggerConfiguration.Build for why.
//
// LOG-5: every configured minimum level (Default + each Override) is backed by a runtime switch,
// adjustable through /api/system/logging/levels without a restart. The logger itself is assembled
// in ApiLoggerConfiguration.Build so the ordering that makes the switches win is under test.
var logLevelSwitches = LogLevelSwitches.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(logLevelSwitches);

// LOG-8: per-line rate limit as a backstop against any single call site flooding the sinks; the
// reporter turns suppressed events into one counted Warning per line per minute.
var logRateLimiter = new LogRateLimiter();
builder.Services.AddSingleton(logRateLimiter);
builder.Services.AddHostedService<LogRateLimitReporter>();

Log.Logger = ApiLoggerConfiguration.Build(builder.Configuration, logLevelSwitches, logRateLimiter).CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();

// Add CORS for development
builder.Services.AddCors(options =>
{
  options.AddPolicy("Development", policy =>
  {
    policy.WithOrigins(
        "http://localhost:5002",
        "https://localhost:5003",
        "http://localhost:5000",
        "https://localhost:5001")
      .AllowAnyMethod()
      .AllowAnyHeader()
      .AllowCredentials();
  });
});

// Add SignalR — tuned for kiosk reliability.
// ClientTimeoutInterval (how long the SERVER waits to hear from a client) is generous: Chrome may
// throttle JS timers when the page is occluded (screen-blanked overlay).
//
// UI-10: KeepAliveInterval (how often the SERVER pings an idle connection) must be at most half the
// CLIENT's ServerTimeout. The only clients of these hubs are radio-web's two HubConnections (Audio,
// Visualization), both on SignalR's default ServerTimeout of 30 s. This was 30 s, equal to it, so any
// connection carrying no other traffic raced its own keepalive. radio-web's visualization connection
// carries no group traffic when it has no subscription or the visualizer is inactive (nothing
// playing); it timed out and reconnected 535 times on 2026-09-27 — about 1,070 of radio-web's 1,781
// journal lines that day. The audio hub was spared only by that day's change-driven traffic; a silent
// box would race it too. 15 s is SignalR's default and its documented pairing with a 30 s
// ServerTimeout. Pinned by SignalRTimeoutTests.
builder.Services.AddSignalR(options =>
{
  options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
  options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

// Add health checks
builder.Services.AddHealthChecks()
  .AddCheck<Radio.API.Health.AudioEngineHealthCheck>("audio-engine")
  .AddCheck<Radio.API.Health.BluetoothHealthCheck>("bluetooth-pipeline");

builder.Services.AddManagedConfiguration(builder.Configuration);
builder.Services.AddMetrics(builder.Configuration);
// Register authoritative MetricDescriptor entries for API-tier metrics
// (PR D #11). Replaces the client-side MapKeyToUnit heuristic for these
// keys; dashboards fall back to the heuristic for any key not described.
builder.Services.AddHostedService<Radio.API.Services.ApiMetricDescriptorRegistration>();
builder.Services.AddFingerprinting(builder.Configuration);
builder.Services.AddSoundFlowAudio(builder.Configuration);
builder.Services.AddRadioServices();
// Weather (NWS) service backing the sleep-screen 3-day forecast. Registers
// IWeatherService + IMemoryCache + named HttpClients ("nws", "weather-zippopotam")
// and binds Display:Weather → WeatherDisplayOptions. See ADR-022.
builder.Services.AddRadioWeather(builder.Configuration);

// GV media fetch + bounded on-disk cache + the API-side X-RotaryPhone-Auth handler (ADR-029 D3/D8).
// Standalone rather than folded into AddSoundFlowAudio; see GvMediaServiceExtensions' remarks.
builder.Services.AddGvMedia(builder.Configuration);

// Attended event playback — the /api/audio/events route family (ADR-029 D1). Standalone for the
// same reason AddGvMedia is; depends on AddSoundFlowAudio and AddGvMedia above for ITTSFactory /
// IDuckingService / AudioFileEventSourceFactory / GvMediaClient.
builder.Services.AddEventPlayback();

// Add diagnostic capture service (+ bind retention options for its output pruning)
builder.Services.Configure<Radio.Core.Configuration.DiagnosticsOptions>(
  builder.Configuration.GetSection(Radio.Core.Configuration.DiagnosticsOptions.SectionName));
builder.Services.AddSingleton<Radio.Infrastructure.Audio.Diagnostics.DiagnosticCaptureService>();

// Add sleep/standby mode service
builder.Services.AddSingleton<SleepService>();
builder.Services.AddSingleton<ISleepService>(sp => sp.GetRequiredService<SleepService>());

// ENC-22: power the panel off after Sleep:PanelOffAfterMinutes on the sleep screen (0 = off, the
// shipped default); any knob wakes it. Linux only — it drives Mutter over the session bus. Registered
// here, ahead of the audio-engine, encoder and phone hosted services (not ahead of every hosted
// service — AddRadioServices and a few above register earlier): its unconditional start-up power-on
// runs before the encoder can deliver input, and because hosted services stop in reverse order it
// stops after the encoder, so its shutdown power-on is the last word.
builder.Services.Configure<Radio.Core.Configuration.PanelPowerOptions>(
  builder.Configuration.GetSection(Radio.Core.Configuration.PanelPowerOptions.SectionName));
if (OperatingSystem.IsLinux())
{
  builder.Services.AddSingleton<Radio.Infrastructure.Platform.Display.IPanelPowerControl,
    Radio.Infrastructure.Platform.Display.MutterPanelPowerControl>();
  builder.Services.AddSingleton(sp => new Radio.Infrastructure.Platform.Display.PanelPowerService(
    sp.GetRequiredService<ILogger<Radio.Infrastructure.Platform.Display.PanelPowerService>>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Radio.Core.Configuration.PanelPowerOptions>>(),
    sp.GetRequiredService<ISleepService>(),
    sp.GetRequiredService<Radio.Infrastructure.Platform.Display.IPanelPowerControl>(),
    sp.GetService<Radio.Core.Interfaces.Input.IRotaryEncoderService>(),
    sp.GetService<TimeProvider>()));
  builder.Services.AddSingleton<IPanelPowerService>(sp =>
    sp.GetRequiredService<Radio.Infrastructure.Platform.Display.PanelPowerService>());
  builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<Radio.Infrastructure.Platform.Display.PanelPowerService>());
}

// Add the audio engine initialization service (must run first)
// Registered as a singleton as well (AUD-37): DevicesController resolves it as
// ICastReconnectControl to cancel the Cast auto-reconnect before a user's own output or Cast
// action. AddHostedService<T>() alone would not make the instance resolvable.
builder.Services.AddSingleton<AudioEngineInitializationService>();
builder.Services.AddSingleton<ICastReconnectControl>(sp => sp.GetRequiredService<AudioEngineInitializationService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AudioEngineInitializationService>());

// Add the visualization broadcast background service
builder.Services.AddHostedService<VisualizationBroadcastService>();

// Add the audio state update background service
builder.Services.AddHostedService<AudioStateUpdateService>();

// Add rotary encoder background service (gated by RotaryEncoder:Enabled)
builder.Services.AddHostedService<RotaryEncoderHostedService>();

// Add phone call integration background service (gated by PhoneIntegration:Enabled)
builder.Services.AddHostedService<PhoneCallIntegrationService>();

builder.Services.PostConfigure<Microsoft.Extensions.Hosting.HostOptions>(_ => { });



var app = builder.Build();

// Configure the HTTP request pipeline.
// API docs are served in every environment, including on the box (OPS-14, owner request 2026-10-02):
// the OpenAPI document at /openapi/v1.json and the Scalar UI at /scalar/v1. This exposes nothing new —
// every endpoint it describes is already reachable, unauthenticated, on the LAN — and Scalar.AspNetCore
// serves its UI script from the package itself, so the page works without internet access.
app.MapOpenApi();
app.MapScalarApiReference();

// Add CORS middleware (must be early in pipeline)
if (app.Environment.IsDevelopment())
{
  app.UseCors("Development");
}

// Add Serilog request logging for better visibility
app.UseSerilogRequestLogging();

// Add API metrics middleware (before other middleware)
app.UseApiMetrics();

// Add audio stream middleware
app.UseAudioStream();

// Only redirect to HTTPS in production (avoids SSL certificate issues in dev)
if (!app.Environment.IsDevelopment())
{
  app.UseHttpsRedirection();
}

app.UseAuthorization();

// Map controllers
app.MapControllers();

// Map SignalR hubs
app.MapHub<AudioVisualizationHub>(ApiPaths.Hubs.Visualization);
app.MapHub<AudioStateHub>(ApiPaths.Hubs.Audio);

// Map health check endpoint
app.MapHealthChecks("/health");

try
{
  // Get log file path from configuration
  // The file sink is nested inside the Async wrapper, so its path lives at
  // WriteTo:0:Args:configure:0:Args:path. This previously read WriteTo:1:Args:path — an index
  // that does not exist and a shape that never matched — so it always fell through to the
  // default. The default happened to be correct, which is why nothing noticed.
  var logPath = builder.Configuration["Serilog:WriteTo:0:Args:configure:0:Args:path"]
    ?? "./logs/radio-.txt";
  var logDirectory = Path.GetDirectoryName(Path.GetFullPath(logPath.Replace(".txt", DateTime.Now.ToString("yyyyMMdd") + ".txt")));

  // Print startup header to console
  Console.WriteLine();
  Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
  Console.WriteLine("║            RADIO CONSOLE API - Starting Up                       ║");
  Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
  Console.WriteLine($"  Log files: {logDirectory}");
  Console.WriteLine($"  Environment: {app.Environment.EnvironmentName}");
  Console.WriteLine();

  // Log startup header to file
  Log.Information("════════════════════════════════════════════════════════════════════");
  Log.Information("  RADIO CONSOLE API - Application Starting");
  Log.Information("  Started at: {Timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
  Log.Information("  Environment: {Environment}", app.Environment.EnvironmentName);
  Log.Information("  Log directory: {LogPath}", logDirectory);
  Log.Information("════════════════════════════════════════════════════════════════════");
  Log.Information("API docs available at /scalar/v1");
  Log.Information("SignalR hubs available at {VizHub} and {AudioHub}", ApiPaths.Hubs.Visualization, ApiPaths.Hubs.Audio);
  Log.Information("Audio stream available at {StreamPath}", ApiPaths.Streams.Audio);
  app.Run();
}
catch (Exception ex)
{
  Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
  Log.CloseAndFlush();
}

// Partial class declaration to enable WebApplicationFactory integration tests
public partial class Program { }
