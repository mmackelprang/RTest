using MudBlazor.Services;
// API client services will be added as they are created in Phase 1
// using Radio.Web.Services.ApiClients;
// using Radio.Web.Services.Hub;

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

// TODO: Register API client services with retry policies (Phase 1 Task 1.2)
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";

// API client services will be registered here once created
// builder.Services.AddHttpClient<AudioApiService>(...)
// builder.Services.AddHttpClient<QueueApiService>(...)
// etc.

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
