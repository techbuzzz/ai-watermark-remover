using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using WatermarkRemover.CLI.Commands;
using WatermarkRemover.Core.Configuration;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;

namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>
/// Wires the HTTP surface of the <c>serve</c> command without binding Kestrel.
/// Three pieces, in the same order <see cref="ServeCommand"/> uses them:
///   1. <see cref="MapEndpoints"/> — JSON endpoints (<c>/health</c>, <c>/clean/*</c>, …)
///   2. <see cref="MountSwagger"/> — <c>/swagger</c> UI + JSON
///   3. <see cref="MountStaticUi"/> — <c>wwwroot/</c> bundle + SPA fallback
/// Extracted from <see cref="ServeCommand"/> so integration tests can exercise
/// the endpoints in-memory via <c>Microsoft.AspNetCore.TestHost.TestServer</c>
/// without spawning the CLI.
/// </summary>
public static class ServeEndpointMapper
{
    /// <summary>
    /// Mounts the eight JSON endpoints on <paramref name="app"/>. The endpoint
    /// behaviour is identical to what <see cref="ServeCommand"/> exposes in
    /// production; the only difference is the dependencies are passed as
    /// parameters instead of being resolved from the command's primary
    /// constructor.
    /// </summary>
    public static void MapEndpoints(
        IEndpointRouteBuilder app,
        ITextCleaningPipeline textPipeline,
        IMarkdownCleaner markdownCleaner,
        IFileCleanerRouter fileRouter,
        IImageCleaningPipeline imagePipeline,
        AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(textPipeline);
        ArgumentNullException.ThrowIfNull(markdownCleaner);
        ArgumentNullException.ThrowIfNull(fileRouter);
        ArgumentNullException.ThrowIfNull(imagePipeline);
        ArgumentNullException.ThrowIfNull(config);

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // POST /clean/text
        app.MapPost("/clean/text", async (TextRequest req, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(req.Text))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Field 'text' is required."));
            }

            TextCleanOptions options = new()
            {
                EnableUnicode = req.EnableUnicode ?? config.Text.Layers.Unicode,
                EnableStatistical = req.EnableStatistical ?? config.Text.Layers.Statistical,
                EnableVendorSpecific = req.EnableVendorSpecific ?? config.Text.Layers.VendorSpecific,
                LlmEndpoint = config.Text.LlmEndpoint,
                LlmModel = config.Text.LlmModel,
            };
            TextCleanResult result = await textPipeline.CleanAsync(req.Text, options, ct).ConfigureAwait(false);
            return Results.Ok(result);
        });

        // POST /detect/text
        app.MapPost("/detect/text", (TextRequest req) =>
        {
            if (string.IsNullOrEmpty(req.Text))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Field 'text' is required."));
            }

            IReadOnlyList<WatermarkMatch> matches = textPipeline.Detect(req.Text);
            return Results.Ok(matches);
        });

        // POST /clean/markdown
        app.MapPost("/clean/markdown", (MarkdownRequest req) =>
        {
            if (string.IsNullOrEmpty(req.Markdown))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Field 'markdown' is required."));
            }

            MarkdownCleanOptions options = req.StripAll == true ? MarkdownCleanOptions.StripAll() : new MarkdownCleanOptions();
            MarkdownCleanResult result = markdownCleaner.Clean(req.Markdown, options);
            return Results.Ok(result);
        });

        // POST /clean/file  (multipart upload)
        app.MapPost("/clean/file", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Upload a file via multipart/form-data."));
            }

            IFormFile file = request.Form.Files[0];
            if (!fileRouter.IsSupported(file.FileName))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.UnsupportedFormat, $"Unsupported file type: {Path.GetExtension(file.FileName)}"));
            }

            string tmpIn = Path.Combine(Path.GetTempPath(), $"wr-in-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
            string tmpOut = Path.Combine(Path.GetTempPath(), $"wr-out-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
            try
            {
                await using (FileStream fs = File.Create(tmpIn))
                {
                    await file.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                FileCleanResult result = fileRouter.Clean(tmpIn, tmpOut, new MetadataCleanOptions());
                byte[] bytes = await File.ReadAllBytesAsync(tmpOut, ct).ConfigureAwait(false);
                return Results.File(bytes, "application/octet-stream", $"cleaned-{file.FileName}");
            }
            finally
            {
                TryDelete(tmpIn);
                TryDelete(tmpOut);
            }
        });

        // GET /inspect/file  (multipart upload via POST is more natural, but spec lists inspect under file ops)
        app.MapPost("/inspect/file", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Upload a file via multipart/form-data."));
            }

            IFormFile file = request.Form.Files[0];
            if (!fileRouter.IsSupported(file.FileName))
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.UnsupportedFormat, $"Unsupported file type: {Path.GetExtension(file.FileName)}"));
            }

            string tmpIn = Path.Combine(Path.GetTempPath(), $"wr-ins-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
            try
            {
                await using (FileStream fs = File.Create(tmpIn))
                {
                    await file.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                IReadOnlyList<MetadataEntry> entries = fileRouter.Inspect(tmpIn);
                return Results.Ok(entries);
            }
            finally
            {
                TryDelete(tmpIn);
            }
        });

        // POST /clean/image  (multipart upload)
        app.MapPost("/clean/image", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Upload an image via multipart/form-data."));
            }

            IFormFile file = request.Form.Files[0];
            string ext = Path.GetExtension(file.FileName);
            string tmpIn = Path.Combine(Path.GetTempPath(), $"wr-img-in-{Guid.NewGuid():N}{ext}");
            string tmpOut = Path.Combine(Path.GetTempPath(), $"wr-img-out-{Guid.NewGuid():N}{ext}");
            try
            {
                await using (FileStream fs = File.Create(tmpIn))
                {
                    await file.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                ImageCleanOptions options = new()
                {
                    ModelPath = config.Image.ModelPath,
                    AutoDetectThreshold = config.Image.AutoDetectThreshold,
                    BlendEdges = config.Image.BlendEdges,
                };
                ImageCleanResult result = await imagePipeline.CleanAsync(tmpIn, tmpOut, options, ct).ConfigureAwait(false);
                byte[] bytes = await File.ReadAllBytesAsync(tmpOut, ct).ConfigureAwait(false);
                return Results.File(bytes, "application/octet-stream", $"cleaned-{file.FileName}");
            }
            finally
            {
                TryDelete(tmpIn);
                TryDelete(tmpOut);
            }
        });

        // POST /detect/image  (multipart upload)
        app.MapPost("/detect/image", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResult(ErrorCodes.InvalidInput, "Upload an image via multipart/form-data."));
            }

            IFormFile file = request.Form.Files[0];
            string ext = Path.GetExtension(file.FileName);
            string tmpIn = Path.Combine(Path.GetTempPath(), $"wr-img-det-{Guid.NewGuid():N}{ext}");
            try
            {
                await using (FileStream fs = File.Create(tmpIn))
                {
                    await file.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                ImageCleanOptions options = new()
                {
                    ModelPath = config.Image.ModelPath,
                    AutoDetectThreshold = config.Image.AutoDetectThreshold,
                };
                IReadOnlyList<DetectedRegion> regions = imagePipeline.Detect(tmpIn, options);
                return Results.Ok(regions);
            }
            finally
            {
                TryDelete(tmpIn);
            }
        });
    }

    /// <summary>
    /// Mounts the OpenAPI / Swagger UI. Requires the caller to have
    /// registered Swagger services with the DI container
    /// (<c>AddEndpointsApiExplorer()</c> + <c>AddSwaggerGen(c => c.SwaggerDoc("v1", …))</c>).
    /// The route prefix is <c>swagger</c>, the JSON lives at
    /// <c>/swagger/v1/swagger.json</c>.
    /// </summary>
    public static void MountSwagger(IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSwagger(c =>
        {
            c.RouteTemplate = "swagger/{documentName}/swagger.{json|yaml}";
        });
        app.UseSwaggerUI(c =>
        {
            c.RoutePrefix = "swagger";
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "WatermarkRemover HTTP API v1");
            c.DocumentTitle = "WatermarkRemover HTTP API";
            c.DisplayRequestDuration();
        });
    }

    /// <summary>
    /// Mounts the bundled Astro web UI on top of the same host. The caller
    /// passes a <see cref="IFileProvider"/> pointing at the directory that
    /// contains the static bundle (production wires a
    /// <see cref="PhysicalFileProvider"/> rooted at <c>wwwroot/</c> next to
    /// the binary; tests can pass one rooted at a temp dir). When
    /// <paramref name="webRoot"/> is <c>null</c> or <paramref name="noUi"/>
    /// is <c>true</c>, the method logs a warning and adds no middleware —
    /// the host then returns 404 for any non-API path.
    /// </summary>
    public static void MountStaticUi(
        IApplicationBuilder app,
        IFileProvider? webRoot,
        bool noUi,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        logger ??= NullLogger.Instance;

        if (noUi || webRoot is null)
        {
            if (!noUi)
            {
                logger.LogWarning(
                    "Web UI bundle not found. Run `npm run build` in /web to bundle it, or pass --no-ui to silence this warning.");
            }
            return;
        }

        // Serve index.html on directory hits, static files for everything else.
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            DefaultFileNames = { "index.html" },
            FileProvider = webRoot,
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = webRoot,
        });
        // SPA-style fallback: any non-API path that didn't match a static
        // file or an API route returns index.html so client-side tab routing
        // (and direct deep links like /#file) keep working.
        IFileProvider provider = webRoot; // capture for the lambda
        app.Use(async (ctx, next) =>
        {
            await next().ConfigureAwait(false);
            // Only the root path gets the SPA fallback; other unmatched paths
            // (e.g. /typo) fall through to 404 instead of always returning
            // index.html.
            if (ctx.Response.StatusCode == StatusCodes.Status404NotFound &&
                ctx.Request.Path == "/" &&
                ctx.Request.Method == HttpMethods.Get)
            {
                IDirectoryContents? contents = provider.GetDirectoryContents("/");
                if (contents is { Exists: true })
                {
                    IFileInfo index = provider.GetFileInfo("index.html");
                    if (index.Exists)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status200OK;
                        ctx.Response.ContentType = "text/html";
                        await ctx.Response.SendFileAsync(index).ConfigureAwait(false);
                    }
                }
            }
        });
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }
}
