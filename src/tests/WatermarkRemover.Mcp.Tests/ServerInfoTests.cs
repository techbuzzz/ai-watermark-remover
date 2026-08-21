using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using WatermarkRemover.Core;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Image;
using WatermarkRemover.Mcp;
using WatermarkRemover.Metadata;
using WatermarkRemover.Text;
using Xunit;

namespace WatermarkRemover.Mcp.Tests;

/// <summary>
/// Unit tests for the DI extension. Verifies that
/// <see cref="McpServiceCollectionExtensions.AddWatermarkRemoverMcp"/>
/// returns a usable <see cref="IMcpServerBuilder"/> with our
/// <see cref="ServerInfo"/> metadata and the assembly-scanned tool
/// set, and that all four pipeline services the tools depend on
/// resolve from the same container.
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddWatermarkRemoverMcp_Returns_IMcpServerBuilder()
    {
        ServiceCollection services = new();
        services.AddSingleton(AppConfig.Default);
        services.AddWatermarkRemoverCore(AppConfig.Default);

        IMcpServerBuilder builder = services.AddWatermarkRemoverMcp();

        builder.Should().NotBeNull();
        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void AddWatermarkRemoverMcp_Configures_ServerInfo()
    {
        // The SDK surfaces the server's name and version through
        // McpServerOptionsSetup. We can assert that the post-built
        // options carry our values by using a tiny test host.
        ServiceCollection services = new();
        services.AddSingleton(AppConfig.Default);
        services.AddWatermarkRemoverCore(AppConfig.Default);
        services.AddWatermarkRemoverMcp();

        ServiceProvider sp = services.BuildServiceProvider();
        IOptions<McpServerOptions>? optionsAccessor = sp.GetServices<IOptions<McpServerOptions>>().FirstOrDefault();

        optionsAccessor.Should().NotBeNull();
        McpServerOptions options = optionsAccessor!.Value;
        options.ServerInfo.Should().NotBeNull();
        options.ServerInfo!.Name.Should().Be(ServerInfo.ServerName);
        options.ServerInfo.Version.Should().Be(ServerInfo.ServerVersion);
    }

    [Fact]
    public void AddWatermarkRemoverMcp_Discovers_ToolsFromAssembly()
    {
        // All 8 tool classes live in this assembly; the SDK's
        // WithToolsFromAssembly scans the calling assembly — which is
        // the WatermarkRemover.Mcp assembly because that's where the
        // tool types were declared.
        ServiceCollection services = new();
        services.AddSingleton(AppConfig.Default);
        services.AddWatermarkRemoverCore(AppConfig.Default);
        services.AddWatermarkRemoverText();
        services.AddWatermarkRemoverMetadata();
        services.AddWatermarkRemoverImage();
        services.AddWatermarkRemoverMcp();

        ServiceProvider sp = services.BuildServiceProvider();
        IReadOnlyList<McpServerTool> tools = sp.GetServices<McpServerTool>().ToList();

        // The 8 tool classes produce 8 McpServerTool instances (each
        // tool class has exactly one [McpServerTool] method).
        tools.Should().HaveCount(8);
        HashSet<string> names = tools.Select(t => t.ProtocolTool?.Name).Where(n => n != null).Select(n => n!).ToHashSet();
        names.Should().BeEquivalentTo(new[]
        {
            "clean_text",
            "clean_markdown",
            "clean_file",
            "clean_image",
            "detect_text",
            "detect_markdown",
            "inspect_file",
            "detect_watermark",
        });
    }

    [Fact]
    public void AddWatermarkRemoverMcp_ResolvesAllRequiredPipelines()
    {
        // The tool methods receive pipeline services via DI parameter
        // binding. We assert that the four service interfaces they
        // depend on are all resolvable from the same container the
        // MCP host will use.
        ServiceCollection services = new();
        services.AddSingleton(AppConfig.Default);
        services.AddWatermarkRemoverCore(AppConfig.Default);
        services.AddWatermarkRemoverText();
        services.AddWatermarkRemoverMetadata();
        services.AddWatermarkRemoverImage();
        services.AddWatermarkRemoverMcp();

        ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<WatermarkRemover.Core.Interfaces.ITextCleaningPipeline>().Should().NotBeNull();
        sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IMarkdownCleaner>().Should().NotBeNull();
        sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IFileCleanerRouter>().Should().NotBeNull();
        sp.GetRequiredService<WatermarkRemover.Core.Interfaces.IImageCleaningPipeline>().Should().NotBeNull();
    }
}
