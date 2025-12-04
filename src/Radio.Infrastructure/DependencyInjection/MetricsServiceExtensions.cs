namespace Radio.Infrastructure.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Metrics.Data;
using Radio.Infrastructure.Metrics.Repositories;
using Radio.Infrastructure.Metrics.Services;

/// <summary>
/// Extension methods for registering metrics services.
/// </summary>
public static class MetricsServiceExtensions
{
  /// <summary>
  /// Adds metrics collection services to the service collection.
  /// </summary>
  /// <param name="services">The service collection</param>
  /// <param name="configuration">The configuration</param>
  /// <returns>The service collection for chaining</returns>
  public static IServiceCollection AddMetrics(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // Bind configuration
    services.Configure<MetricsOptions>(
      configuration.GetSection(MetricsOptions.SectionName));

    // Register core services
    services.AddSingleton<MetricsDbContext>();
    services.AddSingleton<SqliteMetricsRepository>();

    // Register collector (also implements IHostedService)
    services.AddSingleton<BufferedMetricsCollector>();
    services.AddSingleton<IMetricsCollector>(sp => sp.GetRequiredService<BufferedMetricsCollector>());
    services.AddHostedService(sp => sp.GetRequiredService<BufferedMetricsCollector>());

    // Register reader
    services.AddSingleton<IMetricsReader>(sp => sp.GetRequiredService<SqliteMetricsRepository>());

    // Register background services
    services.AddHostedService<MetricsRollupService>();
    services.AddHostedService<SystemMonitorService>();

    return services;
  }
}
