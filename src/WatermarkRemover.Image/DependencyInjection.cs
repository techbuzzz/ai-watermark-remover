using Microsoft.Extensions.DependencyInjection;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;

namespace WatermarkRemover.Image;

/// <summary>DI registration helpers for the image (visual watermark) layer.</summary>
public static class ImageServiceCollectionExtensions
{
    /// <summary>Registers the mask generator, inpainting runner, model downloader and pipeline.</summary>
    public static IServiceCollection AddWatermarkRemoverImage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMaskGenerator, MaskGenerator>();
        services.AddSingleton<IModelDownloader>(_ => new ModelDownloader());

        services.AddSingleton<LamaInpaintingService>(sp =>
        {
            AppConfig config = sp.GetService<AppConfig>() ?? AppConfig.Default;
            return new LamaInpaintingService(config.Image.ModelPath);
        });
        services.AddSingleton<IInpaintRunner>(sp => sp.GetRequiredService<LamaInpaintingService>());
        services.AddSingleton<IInpaintingService>(sp => sp.GetRequiredService<LamaInpaintingService>());

        services.AddSingleton<IImageCleaningPipeline, ImageCleaningPipeline>();
        return services;
    }
}
