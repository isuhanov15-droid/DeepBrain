namespace DeepBrain.Host.Infrastructure;

public static class HostConfigPathResolver
{
    public const string EnvironmentVariable = "DEEPBRAIN_CONFIG_PATH";

    public static string Resolve(
        IReadOnlyList<string> args,
        string baseDirectory,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        string? configuredPath = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                configuredPath = args[++i];
                continue;
            }

            if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                configuredPath = arg["--config=".Length..];
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
            configuredPath = getEnvironmentVariable(EnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(Path.Combine(baseDirectory, "brainconfig.json"));

        return Path.GetFullPath(configuredPath.Trim());
    }
}
