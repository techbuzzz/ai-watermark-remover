using FluentAssertions;
using WatermarkRemover.Core.Configuration;
using Xunit;

namespace WatermarkRemover.Core.Tests;

/// <summary>
/// Tests for <see cref="ServerConfig.MaxUploadMB"/> defaults and shape.
/// The <c>serve</c> command reads this to enforce the 413 guard before
/// streaming multipart bodies to disk.
/// </summary>
public sealed class MaxUploadMBTests
{
    [Fact]
    public void Default_Is100()
    {
        // Matches the documented default in config.yaml and docs/CONFIGURATION.md.
        new ServerConfig().MaxUploadMB.Should().Be(100);
    }

    [Fact]
    public void Default_MatchesConfigYamlDefault()
    {
        // If this breaks, the smoke test that loads src/config.yaml will
        // silently fall back to this value, surprising operators who
        // deleted the key from their config.
        ServerConfig server = new();

        server.MaxUploadMB.Should().Be(100);
    }

    [Fact]
    public void Setter_AssignsValue()
    {
        ServerConfig server = new()
        {
            MaxUploadMB = 25,
        };

        server.MaxUploadMB.Should().Be(25);
    }

    [Fact]
    public void Zero_IsAllowed_ForUnlimited()
    {
        // 0 means "disable the limit" — used by local dev / private
        // deployments that don't want a cap. The CLI validates >= 0.
        ServerConfig server = new()
        {
            MaxUploadMB = 0,
        };

        server.MaxUploadMB.Should().Be(0);
    }

    [Fact]
    public void AppConfig_DefaultCarriesMaxUploadMB()
    {
        AppConfig.Default.Server.MaxUploadMB.Should().Be(100);
    }

    [Fact]
    public void AppConfig_CanOverrideMaxUploadMB()
    {
        AppConfig cfg = new()
        {
            Server = new ServerConfig { MaxUploadMB = 5 },
        };

        cfg.Server.MaxUploadMB.Should().Be(5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(500)]
    public void TypicalValues_RoundTrip(int mb)
    {
        ServerConfig server = new() { MaxUploadMB = mb };

        server.MaxUploadMB.Should().Be(mb);
    }
}