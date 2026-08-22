using Microsoft.Extensions.DependencyInjection;
using WatermarkRemover.Core.Interfaces;

namespace WatermarkRemover.Metadata;

/// <summary>DI registration helpers for the metadata-stripping layer.</summary>
public static class MetadataServiceCollectionExtensions
{
    /// <summary>Registers all file metadata cleaners and the routing service.</summary>
    public static IServiceCollection AddWatermarkRemoverMetadata(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFileMetadataCleaner, JpegMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, PngMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, WebPMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, TiffMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, HeifMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, AvifMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, PdfMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, DocxMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, HtmlMetadataCleaner>();
        services.AddSingleton<IFileMetadataCleaner, EpubMetadataCleaner>();
        services.AddSingleton<IFileCleanerRouter, FileCleanerRouter>();
        return services;
    }
}
