using DeepBrain.Host.Infrastructure;
using Xunit;

namespace DeepBrain.Tests;

public sealed class HostPathsTests
{
    [Fact]
    public void CombinesPathsWithoutHardcodedSeparators()
    {
        var baseDir = Path.Combine("tmp", "deepbrain");
        var logsDir = HostPaths.GetDataDirectory(baseDir, "Logs");
        var filePath = HostPaths.GetLogFilePath(baseDir, "Trace", "trace", new DateTime(2026, 3, 16, 12, 0, 0));

        Assert.Equal(Path.Combine(baseDir, "Logs"), logsDir);
        Assert.Equal(Path.Combine(baseDir, "Trace", "trace-20260316-120000.log"), filePath);
    }
}
