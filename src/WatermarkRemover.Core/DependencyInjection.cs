using Microsoft.Extensions.DependencyInjection;
using WatermarkRemover.Core.Configuration;

namespace WatermarkRemover.Core;

/// <summary>DI registration helpers for the Core layer.</summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the shared <see cref="AppConfig"/> instance used across all layers.</summary>
    public static IServiceCollection AddWatermarkRemoverCore(this IServiceCollection services, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        services.AddSingleton(config);
        return services;
    }
}
