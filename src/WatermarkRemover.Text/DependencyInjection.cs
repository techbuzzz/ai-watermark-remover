using Microsoft.Extensions.DependencyInjection;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Text.Markdown;
using WatermarkRemover.Text.Vendors;

namespace WatermarkRemover.Text;

/// <summary>DI registration helpers for the text-cleaning layer.</summary>
public static class TextServiceCollectionExtensions
{
    /// <summary>Registers Layer A/B/C services, the pipeline and the markdown cleaner.</summary>
    public static IServiceCollection AddWatermarkRemoverText(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IUnicodeHygieneCleaner, UnicodeHygieneCleaner>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IStatisticalWatermarkRewriter>(sp =>
            new StatisticalWatermarkRewriter(sp.GetService<HttpClient>()));

        // Vendor detectors (Layer C).
        services.AddSingleton<IAiTextWatermarkDetector, ClaudeWatermarkDetector>();
        services.AddSingleton<IAiTextWatermarkDetector, GeminiWatermarkDetector>();
        services.AddSingleton<IAiTextWatermarkDetector, OpenAiWatermarkDetector>();
        services.AddSingleton<IAiTextWatermarkDetector, DeepSeekWatermarkDetector>();
        services.AddSingleton<IAiTextWatermarkDetector, GrokWatermarkDetector>();
        services.AddSingleton<IAiTextWatermarkDetector, MistralWatermarkDetector>();

        services.AddSingleton<ITextCleaningPipeline, TextCleaningPipeline>();
        services.AddSingleton<IMarkdownCleaner, MarkdownCleaner>();
        return services;
    }
}
