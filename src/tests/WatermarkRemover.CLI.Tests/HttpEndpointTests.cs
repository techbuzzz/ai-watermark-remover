using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;
using WatermarkRemover.CLI.Commands;
using WatermarkRemover.CLI.Infrastructure;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Metadata;
using WatermarkRemover.Text;
using WatermarkRemover.Text.Markdown;
using WatermarkRemover.Text.Vendors;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// End-to-end tests for the HTTP surface defined in
/// <see cref="ServeEndpointMapper"/>. Uses <see cref="TestServer"/> (from
/// <c>Microsoft.AspNetCore.TestHost</c>) to host the endpoints in-memory —
/// no Kestrel binding, no ports, no <c>dotnet run</c>. Each test builds a
/// fresh <see cref="WebApplication"/> with the same pipeline services the
/// real <c>serve</c> command wires up, and asks <see cref="ServeEndpointMapper"/>
/// to mount the endpoints, Swagger, and the static UI on top.
/// </summary>
public class HttpEndpointTests : IDisposable
{
    private readonly string _tempDir;

    public HttpEndpointTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wr-http-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    // ------------------------------------------------------------------ /health

    [Fact]
    public async Task GetHealth_Returns200WithStatusOk()
    {
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = null;        // no auth
            opts.WithSwagger = false;  // not needed for this test
            opts.WithStaticUi = false;
        });

        HttpResponseMessage response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"ok\"");
    }

    // ------------------------------------------------------------ /clean/text

    [Fact]
    public async Task PostCleanText_RemovesZeroWidthSpace()
    {
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = null;
            opts.WithSwagger = false;
            opts.WithStaticUi = false;
        });

        // The text pipeline's Layer A (UnicodeHygieneCleaner) strips U+200B.
        const string input = "Hello\u200B world";
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clean/text",
            new TextRequest(input, EnableUnicode: true, EnableStatistical: false, EnableVendorSpecific: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TextCleanResult? result = await response.Content.ReadFromJsonAsync<TextCleanResult>();
        result.Should().NotBeNull();
        result!.Cleaned.Should().NotContain("\u200B");
        result.Cleaned.Should().Be("Hello world");
    }

    [Fact]
    public async Task PostCleanText_EmptyText_Returns400()
    {
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = null;
            opts.WithSwagger = false;
            opts.WithStaticUi = false;
        });

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clean/text",
            new TextRequest(string.Empty, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ErrorResult? err = await response.Content.ReadFromJsonAsync<ErrorResult>();
        err.Should().NotBeNull();
        err!.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task PostCleanText_MissingApiKeyHeader_Returns401()
    {
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = "test-secret-key"; // auth on
            opts.WithSwagger = false;
            opts.WithStaticUi = false;
        });

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clean/text",
            new TextRequest("Hello", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostCleanText_WithCorrectApiKeyHeader_Returns200()
    {
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = "test-secret-key";
            opts.WithSwagger = false;
            opts.WithStaticUi = false;
        });
        client.DefaultRequestHeaders.Add("X-API-Key", "test-secret-key");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/clean/text",
            new TextRequest("Hello", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_WithApiKeyEnabled_DoesNotRequireHeader()
    {
        // Per the spec, /health is exempt from the X-API-Key check so monitoring
        // probes don't need credentials.
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = "test-secret-key";
            opts.WithSwagger = false;
            opts.WithStaticUi = false;
        });

        HttpResponseMessage response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- /swagger

    [Fact]
    public async Task GetSwagger_Returns200()
    {
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = null;
            opts.WithSwagger = true;   // mount Swagger
            opts.WithStaticUi = false;
        });

        // Hit the Swagger UI HTML page directly. Swashbuckle issues a 301
        // from `/swagger` to `/swagger/index.html`; the in-memory test host
        // may or may not have a base href that lets HttpClient follow the
        // redirect, so we exercise the page URL itself.
        HttpResponseMessage response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        contentType.Should().Be("text/html");
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("swagger-ui", because: "the Swashbuckle UI bundle should be served at this URL");
    }

    [Fact]
    public async Task GetSwaggerJson_Returns200WithOpenApiSpec()
    {
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = null;
            opts.WithSwagger = true;
            opts.WithStaticUi = false;
        });

        HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        // Machine-readable OpenAPI doc — must at minimum declare the v1 doc.
        body.Should().Contain("\"openapi\"");
        body.Should().Contain("\"paths\"");
    }

    // --------------------------------------------------------------------- /

    [Fact]
    public async Task GetRoot_WhenStaticUiShipped_Returns200WithHtml()
    {
        // Lay down a fake wwwroot with a real index.html.
        string webRoot = Path.Combine(_tempDir, "wwwroot-yes");
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>hi</title>");

        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = null;
            opts.WithSwagger = false;
            opts.WithStaticUi = true;
            opts.WebRootDir = webRoot;
        });

        HttpResponseMessage response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<title>hi</title>");
    }

    [Fact]
    public async Task GetRoot_WhenNoUiFlag_Returns404()
    {
        // WebRoot is set, but --no-ui flips the flag — host should not mount.
        string webRoot = Path.Combine(_tempDir, "wwwroot-yes");
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html>");

        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = null;
            opts.WithSwagger = false;
            opts.WithStaticUi = true;
            opts.NoUi = true;          // <-- the toggle under test
            opts.WebRootDir = webRoot;
        });

        HttpResponseMessage response = await client.GetAsync("/");

        // No API route matches `/`, no static-files middleware mounted, no
        // SPA fallback → ASP.NET Core returns 404.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRoot_WhenWebRootAbsent_Returns404()
    {
        using HttpClient client = BuildClient(opts =>
        {
            opts.ApiKey = null;
            opts.WithSwagger = false;
            opts.WithStaticUi = true;
            opts.NoUi = false;
            opts.WebRootDir = null;    // <-- simulate "wwwroot/ not shipped"
        });

        HttpResponseMessage response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --------------------------------------------------------------- helpers

    private sealed class TestServerOptions
    {
        public string? ApiKey;
        public bool WithSwagger;
        public bool WithStaticUi;
        public bool NoUi;
        public string? WebRootDir;
    }

    /// <summary>
    /// Builds a fresh in-memory HTTP client backed by a started
    /// <see cref="WebApplication"/>. The host configures itself (via
    /// middleware) to call the same <see cref="ServeEndpointMapper"/> methods
    /// the real <c>serve</c> command uses, in the same order. Disposing the
    /// returned client disposes the underlying host.
    /// </summary>
    private static HttpClient BuildClient(Action<TestServerOptions> configure)
    {
        TestServerOptions opts = new();
        configure(opts);

        // Real pipeline services — same shape Program.cs uses. The image
        // pipeline is never invoked by these tests but it has to be present
        // because ServeEndpointMapper.MapEndpoints takes it as a parameter.
        IFileCleanerRouter fileRouter = new FileCleanerRouter(Array.Empty<IFileMetadataCleaner>());

        ITextCleaningPipeline textPipeline = new TextCleaningPipeline(
            new UnicodeHygieneCleaner(),
            new StatisticalWatermarkRewriter(),
            [new ClaudeWatermarkDetector(), new GeminiWatermarkDetector(), new OpenAiWatermarkDetector()]);

        IMarkdownCleaner markdownCleaner = new MarkdownCleaner();

        IImageCleaningPipeline imagePipeline = new StubImagePipeline();
        AppConfig config = AppConfig.Default;

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(textPipeline);
        builder.Services.AddSingleton(markdownCleaner);
        builder.Services.AddSingleton(fileRouter);
        builder.Services.AddSingleton(imagePipeline);
        builder.Services.AddSingleton(config);

        if (opts.WithSwagger)
        {
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "WatermarkRemover HTTP API",
                    Version = "v1",
                });
            });
        }

        WebApplication app = builder.Build();

        // API-key middleware (mirrors ServeCommand.ExecuteAsync).
        if (!string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            string requiredKey = opts.ApiKey;
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/health"))
                {
                    await next().ConfigureAwait(false);
                    return;
                }
                if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var provided) || provided != requiredKey)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new ErrorResult(
                        ErrorCodes.InvalidInput, "Missing or invalid API key.")).ConfigureAwait(false);
                    return;
                }
                await next().ConfigureAwait(false);
            });
        }

        ServeEndpointMapper.MapEndpoints(
            app,
            textPipeline,
            markdownCleaner,
            fileRouter,
            imagePipeline,
            config);

        if (opts.WithSwagger)
        {
            ServeEndpointMapper.MountSwagger(app);
        }

        if (opts.WithStaticUi)
        {
            IFileProvider? provider = !string.IsNullOrEmpty(opts.WebRootDir) && Directory.Exists(opts.WebRootDir)
                ? new PhysicalFileProvider(opts.WebRootDir)
                : null;
            ServeEndpointMapper.MountStaticUi(app, provider, opts.NoUi);
        }

        // StartAsync is required so the test host can resolve services (the
        // TestServer client pulls the request handler from the live IHost,
        // not from the unstarted app).
        app.StartAsync().GetAwaiter().GetResult();

        // app.GetTestClient() returns a HttpClient that disposes the app when
        // the client is disposed — same lifecycle as a TestServer.
        return app.GetTestClient();
    }

    /// <summary>
    /// Inert stand-in for <see cref="IImageCleaningPipeline"/> used only to
    /// satisfy <see cref="ServeEndpointMapper.MapEndpoints"/>'s signature.
    /// None of the tests in this class call the image endpoints; if one
    /// accidentally does, it'll surface as an <see cref="InvalidOperationException"/>
    /// in the test output rather than mysteriously passing.
    /// </summary>
    private sealed class StubImagePipeline : IImageCleaningPipeline
    {
        public Task<ImageCleanResult> CleanAsync(string inputPath, string outputPath, ImageCleanOptions options, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("StubImagePipeline.CleanAsync is not implemented for these tests.");

        public IReadOnlyList<DetectedRegion> Detect(string inputPath, ImageCleanOptions options) =>
            throw new InvalidOperationException("StubImagePipeline.Detect is not implemented for these tests.");
    }
}
