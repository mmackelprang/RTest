using System.Net.Sockets;
using Radzen;
using Radio.Configuration.Bridge;
using Radio.Web;
using Radio.Web.Configuration;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for the Web app.
//
// LOG-1: levels and sinks now come from the `Serilog` section of appsettings.json via
// ReadFrom.Configuration. Previously this hardcoded `.MinimumLevel.Debug()` and never called
// ReadFrom.Configuration at all, so the appsettings logging block was read by nothing — 106 Debug
// sites stayed live in production and the file sink had NO retention cap, measured at 65 MB/day on
// a box that runs for weeks inside a sealed cabinet. Log volume there is not cosmetic: heavy disk
// and journald activity on this hardware correlates with audible audio distortion.
//
// Development still gets Debug, from appsettings.Development.json.
//
// The filter below stays in code because it inspects an exception chain, which the configuration
// syntax cannot express.
Log.Logger = new LoggerConfiguration()
  .ReadFrom.Configuration(builder.Configuration)
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
  .CreateLogger();

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

// GV inter-service auth seam — adds X-RotaryPhone-Auth only when
// RotaryPhone:Gv:AuthKey is set (OFF today). Added to the GV/phone HttpClient
// chains below (ADR-022 §8.1).
builder.Services.AddTransient<Radio.Web.Services.Http.RotaryPhoneAuthHandler>();

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
.AddHttpMessageHandler<Radio.Web.Services.Http.RotaryPhoneAuthHandler>()
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
.AddHttpMessageHandler<Radio.Web.Services.Http.RotaryPhoneAuthHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// GV Bridge SMS send client (same RotaryPhone service). Isolated from the read
// client so the only write path is obvious and trivial to light up — it's the
// flagged send seam (ADR-022 D7). Carries the same auth handler as the other
// radio:5004 clients so the X-RotaryPhone-Auth gate stays consistent.
builder.Services.AddHttpClient<GvBridgeSendService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.AddHttpMessageHandler<Radio.Web.Services.Http.RotaryPhoneAuthHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// GV Trunk API client (same RotaryPhone service). Carries the RotaryPhoneAuthHandler
// too so the X-RotaryPhone-Auth seam is consistent across every radio:5004 client
// — when the gate flips on, all four clients authenticate, not just GV Bridge/Phone.
builder.Services.AddHttpClient<GvTrunkApiService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.AddHttpMessageHandler<Radio.Web.Services.Http.RotaryPhoneAuthHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// Diagnostics API client (same RotaryPhone service) — consumed by PhoneDiagnosticsPanel.
// Same auth seam as the other radio:5004 clients (see GvTrunk comment above).
builder.Services.AddHttpClient<DiagnosticsApiService>(client =>
{
  client.BaseAddress = new Uri(phoneApiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.AddHttpMessageHandler<Radio.Web.Services.Http.RotaryPhoneAuthHandler>()
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

// ENC-4 — encoder HUD state. Sits with the hub services because it takes AudioStateHubService in
// its constructor and subscribes to EncoderHudChanged there. Singleton rather than scoped (unlike
// GainPopoverService): it tracks four physical knobs on one cabinet, so both hosts — MainLayout
// and the /sleep route, which is on a different layout — must see the same card, and it has to
// survive the route change between them.
//
// A singleton nobody injects is never constructed, and an unconstructed one never subscribes to
// the hub. Both hosts inject it: MainLayout renders <EncoderHud> unconditionally, and Sleep.razor
// injects it directly, which covers a kiosk that boots straight onto /sleep.
builder.Services.AddSingleton<Radio.Web.Services.EncoderHudService>();

// GV Messages — UI-local unread count shared with the topbar /phone pill, and
// the single app-wide GV status poll (ADR-022 §6.2). The status service resolves
// GvBridgeApiService through a scope per poll (a singleton can't inject a
// scoped/typed HttpClient), so it's registered with an explicit factory.
builder.Services.AddSingleton<Radio.Web.Services.PhoneUnreadState>();
builder.Services.AddSingleton<Radio.Web.Services.GvBridgeStatusService>(sp =>
  new Radio.Web.Services.GvBridgeStatusService(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<Radio.Web.Services.GvBridgeStatusService>>(),
    builder.Configuration.GetValue("RotaryPhone:Gv:StatusPollSeconds", 10)));
// Drive the poll loop via the host so its lifecycle (start at boot, cancel +
// await at graceful shutdown) is owned by the runtime. AddHostedService does NOT
// register the concrete type, so reuse the singleton above (memory: DI gotcha).
builder.Services.AddHostedService(sp =>
  sp.GetRequiredService<Radio.Web.Services.GvBridgeStatusService>());

// Bell-failure surfacing (handoff §3.7) — the single app-wide poll of the phone
// system-status endpoint, so the topbar /phone fault badge can light up from any
// route without the user having visited /phone first. Same singleton + hosted-service
// + scope-per-poll shape as GvBridgeStatusService above (ADR-022 §6.2).
builder.Services.AddSingleton<Radio.Web.Services.BellHealthService>(sp =>
  new Radio.Web.Services.BellHealthService(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<Radio.Web.Services.BellHealthService>>(),
    builder.Configuration.GetValue("RotaryPhone:BellHealthPollSeconds", 15)));
builder.Services.AddHostedService(sp =>
  sp.GetRequiredService<Radio.Web.Services.BellHealthService>());

// Register centralized audio state store (subscribes to hub, caches state for components)
builder.Services.AddSingleton<AudioStateStore>();

// Register application services
builder.Services.AddScoped<Radio.Web.Services.QueuePersistenceService>();
builder.Services.AddScoped<Radio.Web.Services.DeviceDisplayStateService>();
builder.Services.AddScoped<Radio.Web.Services.RadioPanelToggleService>();

// Task #6 — Messages-feed contact-name resolution. Scoped so the per-circuit
// cache (seeded from the merged contact set, backed by a deduped PBAP lookup)
// is shared by PhonePage and its child panels for the session.
builder.Services.AddScoped<Radio.Web.Services.ContactResolutionService>();

// Task #15 PR E item #47 — gain-popover backdrop portal. Scoped so the
// circuit's NowPlayingPanel + MainLayout share a single instance per user
// session; mounted in MainLayout (OUTSIDE .page-transition) so the backdrop
// escapes the sub-tree stacking context that previously trapped it.
builder.Services.AddScoped<Radio.Web.Services.GainPopoverService>();

// ENC-12. Scoped, like GainPopoverService and unlike AudioStateStore: this tracks what THIS browser
// session has already been told about the knobs, not the state of the knobs themselves.
builder.Services.AddScoped<Radio.Web.Services.EncoderFaultAnnouncer>();

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

// DataProtection key ring. Registered after AddSqliteConfigStore above so a stored
// DataProtection:KeysPath is visible here, matching Radio.API's ordering (its
// AddManagedConfiguration call also follows the bridge registration). Note the
// consequence both services share: configuration.db is bridged into BOTH processes,
// so a DataProtection:KeysPath row written there would move both rings to the same
// directory. Nothing writes that key today, and purpose isolation would still hold
// (different application discriminators), but the separate-directory intent below
// would be lost silently.
//
// Blazor Server protects the serialized marker it emits for each interactive root
// component (one per render-mode boundary), so this process needs a writable key
// ring even though it stores no secrets — see the remarks on DataProtectionSetup.
// With no explicit path, ASP.NET Core falls back to
// $HOME/.aspnet/DataProtection-Keys, and radio-web.service runs with
// ProtectHome=true, which mounts a read-only empty tmpfs over /home while HOME still
// points at /home/mmack. Minting a key then fails with EROFS and every page render
// throws (production outage, 2026-08-16 — see
// design/plans/SECRET-KEYRING-INVESTIGATION.md).
//
// The configured path is relative, and it is resolved against the process WORKING
// DIRECTORY rather than the content root — in production those differ, because
// radio-web.service sets WorkingDirectory=/opt/radio-console and
// ASPNETCORE_CONTENTROOT=/opt/radio-console/web. The working directory is the one we
// want: it yields /opt/radio-console/data/keys-web, which is covered by the unit's
// ReadWritePaths and which the deploy preserves (Deploy-ToLinux.ps1's `rsync
// --delete` targets api/ and web/ only). Resolving against the content root would
// place the ring inside web/, where every deploy would delete it. The base directory
// is passed explicitly so this is a stated choice and not an artifact of the default
// Path.GetFullPath overload.
var dataProtectionKeysPath = DataProtectionSetup.ResolveKeysPath(
  builder.Configuration,
  Directory.GetCurrentDirectory());
builder.Services.AddRadioWebDataProtection(dataProtectionKeysPath);

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

// ENC-12. AudioStateStore subscribes to the hub in its constructor, and a singleton nobody injects
// is never constructed — the same trap Program.cs already documents for EncoderHudService. Until
// this row the store had no consumers at all, so its cache had never run. The encoder fault badge
// seeds from that cache on every circuit start, including one that begins minutes after the fault
// and including the kiosk booting straight onto /sleep, so the cache has to be alive before the
// first circuit rather than because of it.
_ = app.Services.GetRequiredService<AudioStateStore>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error", createScopeForErrors: true);
  app.UseHsts();
}

app.UseHttpsRedirection();

// OPS-5 - static assets revalidate on every request. Bare UseStaticFiles() sent ETag and
// Last-Modified but no Cache-Control, which lets a browser invent its own freshness lifetime
// and reuse a stale asset without asking. See StaticAssetCaching for the measured incident,
// why this is deliberately uniform across every asset class, and why it is not a max-age.
app.UseStaticFiles(StaticAssetCaching.CreateOptions());
app.UseAntiforgery();

// Build identity for deploy verification. Radio.Web's assembly has always carried the git SHA
// (Directory.Build.props stamps it, and Deploy-ToLinux.ps1 already passes -p:SourceRevisionId to
// both publishes) — it was simply unreadable from outside the process, so the deploy could only
// check `systemctl is-active` for this service. That check passes for a *stale* binary, which
// means the first fix that silently fails to land gets debugged as a code bug instead of a
// deploy bug. This endpoint is what closes that gap; it is the Web-side twin of the API's
// /api/health/version and answers on Radio.Web's own port.
app.MapGet("/api/health/version", () =>
  Results.Ok(Radio.Core.Utilities.AssemblyBuildInfo.For(typeof(Program).Assembly)));

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

// The single app-wide GV status poll (drives the Messages reconnecting banner +
// Send gate) is started by the host via IHostedService — see the
// GvBridgeStatusService registration above. No manual Start() needed here.

// Print startup header to console
var logDirectory = Path.GetFullPath("logs");
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║            RADIO CONSOLE WEB - Starting Up                       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  API URL: {apiBaseUrl}");
Console.WriteLine($"  Log files: {logDirectory}");
// Surface the resolved key-ring directory at startup: when DataProtection can't
// write it, the symptom is a 500 on every page with nothing in the header to say
// which directory was attempted.
Console.WriteLine($"  DataProtection keys: {dataProtectionKeysPath}");
Console.WriteLine($"  Environment: {app.Environment.EnvironmentName}");
Console.WriteLine();

// Log startup header to file
Log.Information("════════════════════════════════════════════════════════════════════");
Log.Information("  RADIO CONSOLE WEB - Application Starting");
Log.Information("  Started at: {Timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
Log.Information("  API URL: {ApiUrl}", apiBaseUrl);
Log.Information("  Environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("  Log directory: {LogPath}", logDirectory);
Log.Information("  DataProtection keys: {KeysPath}", dataProtectionKeysPath);
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
