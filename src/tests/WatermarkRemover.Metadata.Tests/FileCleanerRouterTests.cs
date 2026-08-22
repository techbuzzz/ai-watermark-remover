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
        new WebPMetadataCleaner(),
        new TiffMetadataCleaner(),
        new HeifMetadataCleaner(),
        new AvifMetadataCleaner(),
        new PdfMetadataCleaner(),
        new DocxMetadataCleaner(),
        new PptxMetadataCleaner(),
        new XlsxMetadataCleaner(),
        new HtmlMetadataCleaner(),
        new EpubMetadataCleaner(),
    ]);

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.jpg")]
    [InlineData("a.jpeg")]
    [InlineData("a.webp")]
    [InlineData("a.tif")]
    [InlineData("a.tiff")]
    [InlineData("a.heic")]
    [InlineData("a.heif")]
    [InlineData("a.avif")]
    [InlineData("a.pdf")]
    [InlineData("a.docx")]
    [InlineData("a.pptx")]
    [InlineData("a.xlsx")]
    [InlineData("a.html")]
    [InlineData("a.epub")]
    [InlineData("A.PNG")]
    [InlineData("A.TIF")]
    [InlineData("A.HEIC")]
    [InlineData("A.AVIF")]
    [InlineData("A.EPUB")]
    [InlineData("A.PPTX")]
    [InlineData("A.XLSX")]
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
    public void Resolve_WebP_ReturnsWebPCleaner()
    {
        IFileMetadataCleaner? cleaner = BuildRouter().Resolve("photo.webp");
        cleaner.Should().BeOfType<WebPMetadataCleaner>();
    }

    [Fact]
    public void Resolve_Tiff_ReturnsTiffCleaner()
    {
        BuildRouter().Resolve("photo.tif").Should().BeOfType<TiffMetadataCleaner>();
        BuildRouter().Resolve("photo.tiff").Should().BeOfType<TiffMetadataCleaner>();
    }

    [Fact]
    public void Resolve_Heif_ReturnsHeifCleaner()
    {
        BuildRouter().Resolve("photo.heic").Should().BeOfType<HeifMetadataCleaner>();
        BuildRouter().Resolve("photo.heif").Should().BeOfType<HeifMetadataCleaner>();
    }

    [Fact]
    public void Resolve_Avif_ReturnsAvifCleaner()
    {
        BuildRouter().Resolve("photo.avif").Should().BeOfType<AvifMetadataCleaner>();
    }

    [Fact]
    public void Resolve_Epub_ReturnsEpubCleaner()
    {
        BuildRouter().Resolve("book.epub").Should().BeOfType<EpubMetadataCleaner>();
        BuildRouter().Resolve("book.EPUB").Should().BeOfType<EpubMetadataCleaner>();
    }

    [Fact]
    public void Resolve_Pptx_ReturnsPptxCleaner()
    {
        BuildRouter().Resolve("deck.pptx").Should().BeOfType<PptxMetadataCleaner>();
        BuildRouter().Resolve("deck.PPTX").Should().BeOfType<PptxMetadataCleaner>();
    }

    [Fact]
    public void Resolve_Xlsx_ReturnsXlsxCleaner()
    {
        BuildRouter().Resolve("sheet.xlsx").Should().BeOfType<XlsxMetadataCleaner>();
        BuildRouter().Resolve("sheet.XLSX").Should().BeOfType<XlsxMetadataCleaner>();
    }

    [Fact]
    public void SupportedExtensions_AggregatesAllCleaners()
    {
        BuildRouter().SupportedExtensions.Should().Contain([".png", ".jpg", ".webp", ".tif", ".tiff", ".heic", ".heif", ".avif", ".pdf", ".docx", ".pptx", ".xlsx", ".html", ".epub"]);
    }
}
