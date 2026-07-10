namespace DeepBrain.Host.BrainLife.Ml;

public static class MlBackendFactory
{
    public static IBrainMlBackend Create(MlConfig config, Action<string> log)
    {
        var backend = (config.Backend ?? "off").Trim().ToLowerInvariant();
        if (backend == "off" || !config.Enable)
            return new StubBackend();

        if (backend == "remote")
            return new RemoteHostBackend(config, log);

#if ML_CORE
        if (backend == "local")
            return new LocalCoreBackend(config, log);
#endif

        log($"Предупреждение: ML backend '{backend}' недоступен; используется заглушка");
        return new StubBackend();
    }
}
