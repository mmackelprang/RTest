using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radio.Infrastructure.Configuration.Models;
using Radio.Infrastructure.DependencyInjection;
using Radio.Tools.ConfigurationManager;

// Build configuration
var configuration = new ConfigurationBuilder()
  .SetBasePath(Directory.GetCurrentDirectory())
  .AddJsonFile("appsettings.json", optional: true)
  .AddEnvironmentVariables()
  .Build();

// Setup dependency injection
var services = new ServiceCollection();

// Configure logging (minimal for tool)
services.AddLogging(builder =>
{
  builder.SetMinimumLevel(LogLevel.Warning);
  builder.AddConsole();
});

// Add managed configuration services
services.AddManagedConfiguration(configuration);

// Build service provider
var serviceProvider = services.BuildServiceProvider();

// Create and run the interactive tool
var tool = new ConfigurationTool(serviceProvider, configuration);
await tool.RunAsync();
