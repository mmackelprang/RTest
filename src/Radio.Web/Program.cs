using System.Net.Sockets;
using Radzen;
using Radio.Configuration.Bridge;
using Radio.Web;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Serilog;

// Configure Serilog for Web app
Log.Logger = new LoggerConfiguration()
  .MinimumLevel.Debug()
  .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
  .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
  .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
  // Suppress individual "Connection refused" stack traces from API service catch blocks.
  // The ApiConnectionLoggingHandler provides a single throttled WARNING instead.
  .Filter.ByExcluding(logEvent =>
  {
    if (logEvent.Exception is not HttpRequestException httpEx)
    {
      return false;
    }

    var inner = httpEx.InnerException;
    while (inner != null)
    {
      if (inner is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
      {
        return true;
      }

      inner = inner.InnerException;
    }
    return false;
  })
  .Enrich.FromLogContext()
  .WriteTo.Async(a => a.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
  .WriteTo.Async(a => a.File(
    "logs/web-.txt",
    rollingInterval: RollingInterval.Day,
    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"))
  .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

// Add services to the container.
// Note: In .NET 8 Blazor Web Apps, use AddRazorComponents() only - NOT AddServerSideBlazor()
builder.Services.AddRazorComponents()
  .AddInteractiveServerComponents(options =>
  {
    // Enable detailed circuit errors in development
    if (builder.Environment.IsDevelopment())
    {
      options.DetailedErrors = true;
    }

    // Keep disconnected circuits alive longer for kiosk reliability.
    // Default is 3 minutes — extend to 10 minutes so brief network blips
    // or deploy restarts can reconnect without losing circuit state.
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
  });

// Add Radzen Blazor services (dialog, notification, tooltip, context menu)
builder.Services.AddRadzenComponents();

// Register API client services with retry policies (Phase 1 Task 1.2)
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? WebConstants.DefaultApiBaseUrl;

// Throttle connection-refused log spam (logs once per 10s instead of every failed call)
builder.Services.AddTransient<ApiConnectionLoggingHandler>();

// Configure HttpClientHandler to bypass SSL validation in development
void ConfigureHttpClientHandler(HttpMessageHandler handler)
{
  if (handler is HttpClientHandler clientHandler && builder.Environment.IsDevelopment())
  {
    clientHandler.ServerCertificateCustomValidationCallback =
      HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
  }
}

builder.Services.AddHttpClient<AudioApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<SystemApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<QueueApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<PlaylistApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<SourcesApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<ConfigurationApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<DevicesApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<MetricsApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<FileApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<PlayHistoryApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<RadioApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<BluetoothApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<SecretsApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<IntegrationsApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<PbapApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(60); // PBAP sync can take a while
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient("AlbumArtProxy", client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// Weather API client backing the sleep-screen 3-day forecast pane.
// Timeout is shorter than most endpoints because the Sleep page renders the
// pane on a 60-second drift cycle — a slow fetch would block the swap.
builder.Services.AddHttpClient<WeatherApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(15);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// RotaryPhone.API client (separate service on port 5004)
var phoneApiBaseUrl = builder.Configuration.GetValue<string>("RotaryPhone:ApiBaseUrl") ?? "http://radio:5004";
builder.Services.AddHttpClient<PhoneApiService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// GV Bridge API client (same RotaryPhone service)
builder.Services.AddHttpClient<GvBridgeApiService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// GV Trunk API client (same RotaryPhone service)
builder.Services.AddHttpClient<GvTrunkApiService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// Register SignalR hub services as singletons (Phase 1 Task 1.3, Phase 10)
builder.Services.AddSingleton<AudioStateHubService>();
builder.Services.AddSingleton<AudioVisualizationHubService>();
builder.Services.AddSingleton<PhoneHubService>();
builder.Services.AddSingleton<GvTrunkHubService>();

// Register centralized audio state store (subscribes to hub, caches state for components)
builder.Services.AddSingleton<AudioStateStore>();

// Register application services
builder.Services.AddScoped<Radio.Web.Services.QueuePersistenceService>();
builder.Services.AddScoped<Radio.Web.Services.DeviceDisplayStateService>();
builder.Services.AddScoped<Radio.Web.Services.RadioPanelToggleService>();

// Task #15 PR E item #47 — gain-popover backdrop portal. Scoped so the
// circuit's NowPlayingPanel + MainLayout share a single instance per user
// session; mounted in MainLayout (OUTSIDE .page-transition) so the backdrop
// escapes the sub-tree stacking context that previously trapped it.
builder.Services.AddScoped<Radio.Web.Services.GainPopoverService>();

// Visualizer "updates/sec" telemetry. Singleton because the value is shared
// across all visualizer panels and consumed by the dev tray (PR 6).
builder.Services.AddSingleton<Radio.Web.Services.VisualizerTelemetryService>();

// Bind Devices:Aliases → DevicesOptions so MainLayout and DeviceManagementPage can
// inject IOptionsMonitor<DevicesOptions> to clean up raw driver names at render time.
// Defaults to an empty alias map when the section is absent or empty.
// Bridge the SQLite config store into Radio.Web's IConfiguration pipeline so the
// System Config page's saves to Display:* / Radio:Rds:* keys are visible to
// IOptionsMonitor<DisplayOptions> / IOptionsMonitor<RdsScrollOptions> consumers
// (MainLayout topbar clock, Sleep clock, QueueHistoryPanel ends prediction,
// RadioControlPanel RDS scroll). The API process already does this at line 31
// of Radio.API/Program.cs; this is the symmetric Web-side registration.
//
// Path resolution mirrors Radio.API exactly (Database:RootPath +
// Database:ConfigurationSubdirectory + Database:ConfigurationFileName) so both
// services target the same DB file. On the kiosk both services run from
// /opt/radio-console/{api,web}/ with appsettings.json overrides that point at
// the shared ../data/config/configuration.db.
//
// Cross-process caveat: ConfigStoreChangeNotifier.NotifyReload() only fires
// IOptionsMonitor change tokens within the SAME process. Saves originate in
// radio-api, so radio-web sees them on the next circuit init (page reload) —
// not live. Cross-process hot-reload is a deferred follow-up.
var dbSection = builder.Configuration.GetSection("Database");
var rootPath = dbSection["RootPath"] ?? "./data";
var configSubdir = dbSection["ConfigurationSubdirectory"] ?? "config";
var configFile = dbSection["ConfigurationFileName"] ?? "configuration.db";
var configDbPath = Path.GetFullPath(Path.Combine(rootPath, configSubdir, configFile));

var configStoreNotifier = new ConfigStoreChangeNotifier();
builder.Configuration.AddSqliteConfigStore(configDbPath, "sqlite", configStoreNotifier);
builder.Services.AddSingleton(configStoreNotifier);

builder.Services.Configure<DevicesOptions>(builder.Configuration.GetSection(DevicesOptions.SectionName));

// Bind Display:* → DisplayOptions for the wall-clock time-format setting.
// Consumed by MainLayout (topbar Time cluster), Sleep (LED clock), and
// QueueHistoryPanel (ends-~ prediction) via IOptionsMonitor so a user save
// on the System Configuration page repaints all three on the next 1s tick
// without a circuit restart. Defaults to 24h no-seconds (preserves the
// pre-PR behaviour) when the section is absent.
builder.Services.Configure<DisplayOptions>(builder.Configuration.GetSection(DisplayOptions.SectionName));

// HANDOFF-rds-accumulating-scroll — bind Radio:Rds → RdsScrollOptions so
// RadioControlPanel can inject IOptionsMonitor<RdsScrollOptions> and react
// to SQLite-store writes (PR #298 config bridge) without a page reload.
// Defaults (256 chars, 40 px/s, " • ") apply when the section is absent.
builder.Services.Configure<RdsScrollOptions>(builder.Configuration.GetSection(RdsScrollOptions.SectionName));

// Bind Display:Weather → WeatherDisplayOptions for the sleep-screen forecast
// pane. Consumed by Sleep.razor's refresh loop (lazy — only when the next
// swap-cycle is forecast-side, no eager fetching). Hot-reloads through the
// same SQLite-bridge + IOptionsMonitor pipeline as DisplayOptions.
builder.Services.Configure<Radio.Core.Configuration.WeatherDisplayOptions>(
  builder.Configuration.GetSection(Radio.Core.Configuration.WeatherDisplayOptions.SectionName));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error", createScopeForErrors: true);
  app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// Proxy album art requests to the API server.
// Album art URLs from SignalR are relative (/api/albumart/{file}) and resolve against
// the Web server origin. The API server owns the file cache, so we proxy to it.
app.MapGet("/api/albumart/{filename}", async (string filename, IHttpClientFactory httpClientFactory) =>
{
  // Sanitize: prevent path traversal
  if (string.IsNullOrWhiteSpace(filename) ||
      filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
      filename.Contains("..") || filename.Contains('/') || filename.Contains('\\'))
  {
    return Results.NotFound();
  }

  try
  {
    var client = httpClientFactory.CreateClient("AlbumArtProxy");
    var response = await client.GetAsync($"/api/albumart/{filename}");
    if (!response.IsSuccessStatusCode)
    {
      return Results.NotFound();
    }

    var bytes = await response.Content.ReadAsByteArrayAsync();
    var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
    return Results.File(bytes, contentType);
  }
  catch
  {
    return Results.NotFound();
  }
});

app.MapRazorComponents<Radio.Web.Components.App>()
  .AddInteractiveServerRenderMode();

// Start RotaryPhone hub connections (non-blocking — logs warning if unavailable)
var phoneHub = app.Services.GetRequiredService<PhoneHubService>();
_ = phoneHub.StartAsync();
var gvTrunkHub = app.Services.GetRequiredService<GvTrunkHubService>();
_ = gvTrunkHub.StartAsync();

// Print startup header to console
var logDirectory = Path.GetFullPath("logs");
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║            RADIO CONSOLE WEB - Starting Up                       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  API URL: {apiBaseUrl}");
Console.WriteLine($"  Log files: {logDirectory}");
Console.WriteLine($"  Environment: {app.Environment.EnvironmentName}");
Console.WriteLine();

// Log startup header to file
Log.Information("════════════════════════════════════════════════════════════════════");
Log.Information("  RADIO CONSOLE WEB - Application Starting");
Log.Information("  Started at: {Timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
Log.Information("  API URL: {ApiUrl}", apiBaseUrl);
Log.Information("  Environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("  Log directory: {LogPath}", logDirectory);
Log.Information("════════════════════════════════════════════════════════════════════");

try
{
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

public partial class Program { }
