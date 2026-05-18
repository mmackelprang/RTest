namespace Radio.Metrics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Metrics.Data;
using Radio.Metrics.Repositories;
using Radio.Metrics.Services;

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

    // Register descriptor registry (PR D #11 — replaces the client-side
    // MapKeyToUnit heuristic with an authoritative server-registered unit
    // table). Singleton so producers (background services, controllers) and
    // consumers (dashboards via the API) share the same map for the
    // process lifetime.
    services.AddSingleton<IMetricDescriptorRegistry, MetricDescriptorRegistry>();

    // Register background services
    services.AddHostedService<MetricsRollupService>();
    services.AddHostedService<SystemMonitorService>();

    return services;
  }
}
