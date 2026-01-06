using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces;
using Radio.Infrastructure.Services;

namespace Radio.Infrastructure.DependencyInjection;

public static class RadioServiceExtensions
{
    public static IServiceCollection AddRadioServices(this IServiceCollection services)
    {
        services.AddSingleton<IRadioBandService, RadioBandService>();
        return services;
    }
}
