using System.Reflection;
using Microsoft.OpenApi.Models;
using Radio.API.Hubs;
using Radio.API.Middleware;
using Radio.API.Services;
using Radio.API.Streaming;
using Radio.Infrastructure.DependencyInjection;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
  .ReadFrom.Configuration(builder.Configuration)
  .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
  options.SwaggerDoc("v1", new OpenApiInfo
  {
    Title = "Radio Console API",
    Version = "v1",
    Description = "REST API for Grandpa Anderson's Console Radio Remade - A modern audio command center.",
    Contact = new OpenApiContact
    {
      Name = "Radio Console Project"
    },
    License = new OpenApiLicense
    {
      Name = "MIT License"
    }
  });

  // Include XML comments for API documentation
  var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
  var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
  if (File.Exists(xmlPath))
  {
    options.IncludeXmlComments(xmlPath);
  }
});

// Add SignalR
builder.Services.AddSignalR();

// Add Metrics services
builder.Services.AddMetrics(builder.Configuration);

// Add Audio Infrastructure services
builder.Services.AddSoundFlowAudio(builder.Configuration);

// Add Radio Hardware services
// TODO: Re-enable when RadioProtocol.Core is available
// builder.Services.AddRadioHardware(builder.Configuration);

// Add Fingerprinting services (for play history)
builder.Services.AddFingerprinting(builder.Configuration);

// Add Configuration Infrastructure services
builder.Services.AddManagedConfiguration(builder.Configuration);

// Add External Services (Spotify authentication, etc.)
builder.Services.AddSpotifyServices();

// Add the visualization broadcast background service
builder.Services.AddHostedService<VisualizationBroadcastService>();

// Add the audio state update background service
builder.Services.AddHostedService<AudioStateUpdateService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI(options =>
  {
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Radio Console API v1");
    options.RoutePrefix = "swagger";
  });
}

// Add API metrics middleware (before other middleware)
app.UseApiMetrics();

// Add audio stream middleware
app.UseAudioStream();

app.UseHttpsRedirection();

app.UseAuthorization();

// Map controllers
app.MapControllers();

// Map SignalR hubs
app.MapHub<AudioVisualizationHub>("/hubs/visualization");
app.MapHub<AudioStateHub>("/hubs/audio");

try
{
  Log.Information("Starting Radio Console API");
  Log.Information("Swagger UI available at /swagger");
  Log.Information("SignalR hubs available at /hubs/visualization and /hubs/audio");
  Log.Information("Audio stream available at /stream/audio");
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
