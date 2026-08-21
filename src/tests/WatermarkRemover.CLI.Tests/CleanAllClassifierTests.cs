using FluentAssertions;
using WatermarkRemover.CLI.Commands;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Core.Models;
using WatermarkRemover.Metadata;
using Xunit;

namespace WatermarkRemover.CLI.Tests;

/// <summary>
/// Unit tests for the routing decision in <c>clean-all</c>. The classifier
/// is the only piece of business logic that doesn't already have its own
/// test project, so we cover it directly here — the rest of the command
/// is plumbing around the existing pipelines.
/// </summary>
public class CleanAllClassifierTests
{
    private static IFileCleanerRouter BuildRouter() => new FileCleanerRouter(
    [
        new JpegMetadataCleaner(),
        new PngMetadataCleaner(),
        new WebPMetadataCleaner(),
        new PdfMetadataCleaner(),
        new DocxMetadataCleaner(),
        new HtmlMetadataCleaner(),
    ]);

    [Theory]
    [InlineData("readme.md")]
    [InlineData("README.MD")]
    [InlineData("notes.markdown")]
    [InlineData("nested/path/to/post.Markdown")]
    public void Classify_MarkdownExtensions_RouteToMarkdownPipeline(string path)
    {
        CleanAllClassifier.Pipeline pipeline = CleanAllClassifier.Classify(path, BuildRouter());
        pipeline.Should().Be(CleanAllClassifier.Pipeline.Markdown);
    }

    [Theory]
    [InlineData("photo.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("doc.pdf")]
    [InlineData("page.html")]
    [InlineData("report.webp")]
    [InlineData("file.docx")]
    public void Classify_RouterSupportedExtensions_RouteToFileMetadata(string path)
    {
        CleanAllClassifier.Pipeline pipeline = CleanAllClassifier.Classify(path, BuildRouter());
        pipeline.Should().Be(CleanAllClassifier.Pipeline.FileMetadata);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("notes.text")]
    [InlineData("app.log")]
    [InlineData("NOTES.TXT")]
    public void Classify_TextExtensions_RouteToTextPipeline(string path)
    {
        CleanAllClassifier.Pipeline pipeline = CleanAllClassifier.Classify(path, BuildRouter());
        pipeline.Should().Be(CleanAllClassifier.Pipeline.Text);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("Makefile")]
    [InlineData("Dockerfile")]
    public void Classify_NoExtension_RouteToTextPipeline(string path)
    {
        // Files with no extension are best-effort treated as text; users can
        // skip with --dry-run if that misclassification matters.
        CleanAllClassifier.Pipeline pipeline = CleanAllClassifier.Classify(path, BuildRouter());
        pipeline.Should().Be(CleanAllClassifier.Pipeline.Text);
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("program.exe")]
    [InlineData("library.dll")]
    [InlineData("movie.mp4")]
    [InlineData("binary.bin")]
    [InlineData("font.ttf")]
    public void Classify_UnknownBinaryExtensions_RouteToUnsupported(string path)
    {
        // Binary formats the router doesn't know must NOT be silently
        // piped into the text pipeline — see the Risks note in WR-S3.
        CleanAllClassifier.Pipeline pipeline = CleanAllClassifier.Classify(path, BuildRouter());
        pipeline.Should().Be(CleanAllClassifier.Pipeline.Unsupported);
    }

    [Fact]
    public void Classify_MarkdownWinsOverRouterEvenIfRouterSupportsExtension()
    {
        // Edge case: if a future router added .md support, markdown still
        // wins because we check it first. Use a fake router to verify
        // ordering without coupling to the real router's behaviour.
        IFileCleanerRouter fakeRouter = new FakeRouter([".md"]);
        CleanAllClassifier.Pipeline pipeline = CleanAllClassifier.Classify("post.md", fakeRouter);
        pipeline.Should().Be(CleanAllClassifier.Pipeline.Markdown);
    }

    [Fact]
    public void Classify_NullPath_Throws()
    {
        Action act = () => CleanAllClassifier.Classify(null!, BuildRouter());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Classify_NullRouter_Throws()
    {
        Action act = () => CleanAllClassifier.Classify("a.txt", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Minimal stub so we can test classifier ordering without a real router.</summary>
    private sealed class FakeRouter : IFileCleanerRouter
    {
        private readonly HashSet<string> _extensions;

        public FakeRouter(IEnumerable<string> extensions)
        {
            _extensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            SupportedExtensions = _extensions.ToArray();
        }

        public IReadOnlyCollection<string> SupportedExtensions { get; }

        public IFileMetadataCleaner? Resolve(string path) => null;

        public bool IsSupported(string path) => _extensions.Contains(Path.GetExtension(path));

        public FileCleanResult Clean(string inputPath, string outputPath, MetadataCleanOptions options) =>
            throw new NotSupportedException();

        public IReadOnlyList<MetadataEntry> Inspect(string inputPath) => [];
    }
}
