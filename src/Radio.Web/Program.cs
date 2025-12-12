using MudBlazor;
using MudBlazor.Services;
using Radio.Web.Services.ApiClients;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
  .AddInteractiveServerComponents();

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

builder.Services.AddHttpClient<AudioApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<SystemApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
});

// TODO: Add remaining API client services
// - QueueApiService (6 endpoints)
// - SpotifyApiService (10 endpoints)
// - FileApiService (3 endpoints)
// - RadioApiService (23 endpoints)
// - SourcesApiService (5 endpoints)
// - DevicesApiService (7 endpoints)
// - MetricsApiService (5 endpoints)
// - PlayHistoryApiService (8 endpoints)
// - ConfigurationApiService (5 endpoints)

// TODO: Register SignalR hub service as singleton (Phase 1 Task 1.3)
// builder.Services.AddSingleton<AudioStateHubService>();

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

app.Run();
