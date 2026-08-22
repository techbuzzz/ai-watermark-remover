using FluentAssertions;
using WatermarkRemover.Core.Configuration;
using Xunit;

namespace WatermarkRemover.Core.Tests;

/// <summary>
/// Tests for the <see cref="RateLimitConfig"/> defaults and shape.
/// The defaults here are load-bearing: the CLI's <c>serve</c> command
/// pre-flight validation compares against them, and operators who
/// delete a key from <c>config.yaml</c> get these values back.
/// </summary>
public sealed class RateLimitConfigTests
{
    [Fact]
    public void Default_PermitLimit_Is100()
    {
        // Matches the previous hard-coded value in ServeCommand.cs
        // before this became configurable.
        new RateLimitConfig().PermitLimit.Should().Be(100);
    }

    [Fact]
    public void Default_WindowSeconds_Is60()
    {
        new RateLimitConfig().WindowSeconds.Should().Be(60);
    }

    [Fact]
    public void Default_QueueLimit_IsZero()
    {
        // 0 = reject immediately on overflow (HTTP 429 with no queueing).
        new RateLimitConfig().QueueLimit.Should().Be(0);
    }

    [Fact]
    public void Default_AllThree_AreSane()
    {
        RateLimitConfig rl = new();

        rl.PermitLimit.Should().BeGreaterThan(0, "the limiter must allow at least one request");
        rl.WindowSeconds.Should().BeGreaterThan(0, "the window must have a positive length");
        rl.QueueLimit.Should().BeGreaterThanOrEqualTo(0, "0 means no queueing, which is valid");
    }

    [Fact]
    public void Setters_AssignValues()
    {
        RateLimitConfig rl = new()
        {
            PermitLimit = 5,
            WindowSeconds = 10,
            QueueLimit = 3,
        };

        rl.PermitLimit.Should().Be(5);
        rl.WindowSeconds.Should().Be(10);
        rl.QueueLimit.Should().Be(3);
    }

    [Fact]
    public void ServerConfig_DefaultHasRateLimit()
    {
        ServerConfig server = new();

        server.RateLimit.Should().NotBeNull();
    }

    [Fact]
    public void ServerConfig_ReplacingRateLimit_Propagates()
    {
        ServerConfig server = new()
        {
            RateLimit = new RateLimitConfig
            {
                PermitLimit = 42,
                WindowSeconds = 7,
                QueueLimit = 1,
            },
        };

        server.RateLimit.PermitLimit.Should().Be(42);
        server.RateLimit.WindowSeconds.Should().Be(7);
        server.RateLimit.QueueLimit.Should().Be(1);
    }
}
