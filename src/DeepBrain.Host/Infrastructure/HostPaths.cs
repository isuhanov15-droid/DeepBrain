namespace DeepBrain.Host.Infrastructure;

public static class HostPaths
{
    public static string GetDataDirectory(string baseDirectory, string folderName)
    {
        return Path.Combine(baseDirectory, folderName);
    }

    public static string GetLogFilePath(string baseDirectory, string folderName, string filePrefix, DateTime startTime)
    {
        var dir = GetDataDirectory(baseDirectory, folderName);
        return Path.Combine(dir, $"{filePrefix}-{startTime:yyyyMMdd-HHmmss}.log");
    }
}
