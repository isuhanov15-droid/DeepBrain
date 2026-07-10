using DeepBrain.Host.Infrastructure;
using Xunit;

namespace DeepBrain.Tests;

public sealed class HostConfigPathResolverTests
{
    [Fact]
    public void UsesConfigNextToHostByDefault()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "deepbrain-host");

        var path = HostConfigPathResolver.Resolve(
            Array.Empty<string>(),
            baseDirectory,
            _ => null);

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDirectory, "brainconfig.json")), path);
    }

    [Fact]
    public void ReadsConfigPathFromEnvironment()
    {
        var configured = Path.Combine(Path.GetTempPath(), "deepbrain", "runtime.json");

        var path = HostConfigPathResolver.Resolve(
            Array.Empty<string>(),
            Path.GetTempPath(),
            name => name == HostConfigPathResolver.EnvironmentVariable ? configured : null);

        Assert.Equal(Path.GetFullPath(configured), path);
    }

    [Fact]
    public void CommandLineConfigOverridesEnvironment()
    {
        var commandLinePath = Path.Combine(Path.GetTempPath(), "deepbrain", "command-line.json");
        var environmentPath = Path.Combine(Path.GetTempPath(), "deepbrain", "environment.json");

        var path = HostConfigPathResolver.Resolve(
            new[] { "--config", commandLinePath },
            Path.GetTempPath(),
            _ => environmentPath);

        Assert.Equal(Path.GetFullPath(commandLinePath), path);
    }

    [Fact]
    public void SupportsEqualsSyntax()
    {
        var configured = Path.Combine(Path.GetTempPath(), "deepbrain", "equals.json");

        var path = HostConfigPathResolver.Resolve(
            new[] { $"--config={configured}" },
            Path.GetTempPath(),
            _ => null);

        Assert.Equal(Path.GetFullPath(configured), path);
    }
}
