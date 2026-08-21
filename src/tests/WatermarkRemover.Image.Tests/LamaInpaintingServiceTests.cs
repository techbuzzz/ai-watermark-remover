using FluentAssertions;
using WatermarkRemover.Image;
using Xunit;

namespace WatermarkRemover.Image.Tests;

public class LamaInpaintingServiceTests
{
    [Fact]
    public void IsAvailable_MissingModel_IsFalse()
    {
        using var service = new LamaInpaintingService("./does-not-exist.onnx");
        service.IsAvailable.Should().BeFalse();
        service.ModelName.Should().Be("none");
    }

    [Fact]
    public void IsModelAvailable_MissingPath_ReturnsFalse()
    {
        using var service = new LamaInpaintingService("./does-not-exist.onnx");
        service.IsModelAvailable("./nope.onnx").Should().BeFalse();
    }
}
