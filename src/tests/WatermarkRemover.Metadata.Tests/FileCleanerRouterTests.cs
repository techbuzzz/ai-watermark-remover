using FluentAssertions;
using WatermarkRemover.Core.Interfaces;
using WatermarkRemover.Metadata;
using Xunit;

namespace WatermarkRemover.Metadata.Tests;

public class FileCleanerRouterTests
{
    private static FileCleanerRouter BuildRouter() => new(
    [
        new JpegMetadataCleaner(),
        new PngMetadataCleaner(),
        new PdfMetadataCleaner(),
        new DocxMetadataCleaner(),
        new HtmlMetadataCleaner(),
    ]);

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.jpg")]
    [InlineData("a.jpeg")]
    [InlineData("a.pdf")]
    [InlineData("a.docx")]
    [InlineData("a.html")]
    [InlineData("A.PNG")]
    public void IsSupported_KnownExtensions_ReturnsTrue(string path)
    {
        BuildRouter().IsSupported(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.exe")]
    [InlineData("a")]
    public void IsSupported_UnknownExtensions_ReturnsFalse(string path)
    {
        BuildRouter().IsSupported(path).Should().BeFalse();
    }

    [Fact]
    public void Resolve_Png_ReturnsPngCleaner()
    {
        IFileMetadataCleaner? cleaner = BuildRouter().Resolve("photo.png");
        cleaner.Should().BeOfType<PngMetadataCleaner>();
    }

    [Fact]
    public void SupportedExtensions_AggregatesAllCleaners()
    {
        BuildRouter().SupportedExtensions.Should().Contain([".png", ".jpg", ".pdf", ".docx", ".html"]);
    }
}
