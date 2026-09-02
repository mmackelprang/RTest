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
// LOG-11: the console sink is restricted to Warning. Under systemd, stdout is captured by
// journald, so an unrestricted console sink meant every Information line was written twice — once
// to the journal and once to the file sink — on a box where log volume is an audio problem, not a
// disk problem. Dropping the sink outright was the other option in the row and is the wrong half:
// it would take the journald priority path with it, and `journalctl -p` is how this box gets
// triaged remotely. Warnings and errors still reach the journal; the file keeps full Information
// detail.
Log.Logger = new LoggerConfiguration()
  .ReadFrom.Configuration(builder.Configuration)
  .WriteTo.Async(a => a.Console(
    new SystemdConsoleFormatter(),
    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning))
  .CreateLogger();

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
// Chrome may throttle JS timers when the page is visually occluded (screen-blanked overlay),
// so we allow generous timeouts to avoid killing the circuit during idle screen-blank.
builder.Services.AddSignalR(options =>
{
  options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
  options.KeepAliveInterval = TimeSpan.FromSeconds(30);
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

// Add diagnostic capture service (+ bind retention options for its output pruning)
builder.Services.Configure<Radio.Core.Configuration.DiagnosticsOptions>(
  builder.Configuration.GetSection(Radio.Core.Configuration.DiagnosticsOptions.SectionName));
builder.Services.AddSingleton<Radio.Infrastructure.Audio.Diagnostics.DiagnosticCaptureService>();

// Add sleep/standby mode service
builder.Services.AddSingleton<SleepService>();
builder.Services.AddSingleton<ISleepService>(sp => sp.GetRequiredService<SleepService>());

// Add the audio engine initialization service (must run first)
builder.Services.AddHostedService<AudioEngineInitializationService>();

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
if (app.Environment.IsDevelopment())
{
  app.MapOpenApi();
  app.MapScalarApiReference();
}

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
