using MudBlazor;
using MudBlazor.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Services.Hub;
using Serilog;

// Configure Serilog for Web app
Log.Logger = new LoggerConfiguration()
  .MinimumLevel.Debug()
  .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
  .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
  .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
  .Enrich.FromLogContext()
  .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
  .WriteTo.File(
    "logs/web-.txt",
    rollingInterval: RollingInterval.Day,
    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
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
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";

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
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

builder.Services.AddHttpClient<SpotifyApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
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
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});

// All 11 API client services are now registered!
// Total: 86 REST endpoints implemented

// Register SignalR hub services as singletons (Phase 1 Task 1.3, Phase 10)
builder.Services.AddSingleton<AudioStateHubService>();
builder.Services.AddSingleton<AudioVisualizationHubService>();

// Register application services
builder.Services.AddScoped<Radio.Web.Services.QueuePersistenceService>();

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
