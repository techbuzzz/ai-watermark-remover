using FluentAssertions;
using WatermarkRemover.Core.Configuration;
using Xunit;

namespace WatermarkRemover.Core.Tests;

/// <summary>
/// Smoke tests for the typed <see cref="AppConfig"/> graph. These cover
/// the defaults the rest of the CLI relies on when no
/// <c>config.yaml</c> is present.
/// </summary>
public sealed class AppConfigTests
{
    [Fact]
    public void Default_ExposesEverySection()
    {
        AppConfig cfg = AppConfig.Default;

        cfg.Text.Should().NotBeNull();
        cfg.Markdown.Should().NotBeNull();
        cfg.Image.Should().NotBeNull();
        cfg.Metadata.Should().NotBeNull();
        cfg.Logging.Should().NotBeNull();
        cfg.Server.Should().NotBeNull();
    }

    [Fact]
    public void Default_Server_HasRateLimit()
    {
        AppConfig cfg = AppConfig.Default;

        cfg.Server.RateLimit.Should().NotBeNull();
    }

    [Fact]
    public void Default_Server_HasMaxUploadMB()
    {
        AppConfig cfg = AppConfig.Default;

        cfg.Server.MaxUploadMB.Should().Be(100);
    }

    [Fact]
    public void Default_Server_MaxUploadMB_IsPositive()
    {
        // The upload guard only activates when MaxUploadMB > 0; the
        // default must be a sane positive value so public deployments
        // aren't wide-open by default.
        AppConfig.Default.Server.MaxUploadMB.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Default_LoggingLevel_IsInformation()
    {
        // The "silent by default" promise — operators who don't touch
        // config.yaml shouldn't get spammed.
        AppConfig.Default.Logging.Level.Should().Be("Information");
        AppConfig.Default.Logging.Output.Should().Be("console");
    }

    [Fact]
    public void Default_TextLayerDefaults_MatchHistoricalBehavior()
    {
        // Layer A + C on, Layer B off. These are the values the docs
        // document and the only sane starting point.
        AppConfig.Default.Text.Layers.Unicode.Should().BeTrue();
        AppConfig.Default.Text.Layers.Statistical.Should().BeFalse();
        AppConfig.Default.Text.Layers.VendorSpecific.Should().BeTrue();
    }

    [Fact]
    public void Default_ImageConfig_PointsAtLaMaModel()
    {
        AppConfig.Default.Image.ModelPath.Should().EndWith(".onnx");
        AppConfig.Default.Image.AutoDetectThreshold.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void Default_AllInstancesShareTheSameSingleton()
    {
        // AppConfig.Default is a static singleton — the CLI relies on
        // reference equality when the file is missing.
        AppConfig.Default.Should().BeSameAs(AppConfig.Default);
    }
}
