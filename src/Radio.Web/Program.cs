using System.Net.Sockets;
using MudBlazor;
using MudBlazor.Services;
using Radio.Web;
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

// Add MudBlazor services
builder.Services.AddMudServices(config =>
{
  config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
  config.SnackbarConfiguration.ShowCloseIcon = true;
  config.SnackbarConfiguration.VisibleStateDuration = 3000;
  config.SnackbarConfiguration.PreventDuplicates = false;
  config.SnackbarConfiguration.NewestOnTop = true;
  config.SnackbarConfiguration.ShowTransitionDuration = 300;
  config.SnackbarConfiguration.HideTransitionDuration = 300;
});

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

// All 13 API client services are now registered!

// Register SignalR hub services as singletons (Phase 1 Task 1.3, Phase 10)
builder.Services.AddSingleton<AudioStateHubService>();
builder.Services.AddSingleton<AudioVisualizationHubService>();

// Register application services
builder.Services.AddScoped<Radio.Web.Services.QueuePersistenceService>();
builder.Services.AddScoped<Radio.Web.Services.DeviceDisplayStateService>();
builder.Services.AddScoped<Radio.Web.Services.RadioPanelToggleService>();

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
