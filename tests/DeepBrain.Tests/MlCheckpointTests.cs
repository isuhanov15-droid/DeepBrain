using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Host.BrainLife.Ml;
using Xunit;

namespace DeepBrain.Tests;

public sealed class MlCheckpointTests
{
    [Fact]
    public void RestoredTrainingStepsImmediatelyRestorePolicyWeight()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "brain_ml.chk");
            var backendPath = "checkpoints/brain_ml.chk.net";
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                EpisodeId = 75,
                Epsilon = 0.05,
                TrainSteps = 2_884,
                WeightsPath = backendPath
            }));

            var config = BrainConfig.Default.Ml with
            {
                Enable = true,
                Backend = "remote",
                NetWeightMax = 0.6,
                NetWeightWarmup = 5_000
            };
            var backend = new FakeBackend("remote", trainSteps: 0);
            var advisor = new MlPolicyAdvisor(config, _ => { }, backend);

            Assert.True(advisor.TryLoad(path, out var episodeId));
            Assert.Equal(75, episodeId);
            Assert.Equal(backendPath, backend.LoadedPath);

            var heuristicScores = ActionCatalog.Actions.ToDictionary(action => action, _ => 0.5);
            var mask = Enumerable.Repeat(1f, ActionCatalog.Count).ToArray();
            var decision = advisor.SelectAction(
                new float[StateVectorizer.InputDim],
                heuristicScores,
                ActionCatalog.Actions,
                mask,
                config,
                tick: 1);
            var telemetry = advisor.BuildTelemetry(
                enabled: true,
                coreAvailable: false,
                inputDim: StateVectorizer.InputDim,
                actionCount: ActionCatalog.Count,
                avgReward200: 0,
                reasonIfDisabled: null,
                mlMode: "evaluation",
                trainEnabled: false,
                trainingEpisodeCount: 0,
                evalEpisodeCount: 0);

            Assert.True(decision.UsedMl);
            Assert.True(decision.NetWeight > 0.30);
            Assert.Equal(2_884, telemetry.TrainSteps);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RemoteSaveSeparatesWrapperFromServiceMetadata()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "brain_ml.chk");
            var config = BrainConfig.Default.Ml with { Enable = true, Backend = "remote" };
            var backend = new FakeBackend("remote", trainSteps: 3_344);
            var advisor = new MlPolicyAdvisor(config, _ => { }, backend);

            advisor.TrySave(path, episodeId: 81);

            Assert.Equal(path + ".remote", backend.SavedPath);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(path + ".remote", json.RootElement.GetProperty("WeightsPath").GetString());
            Assert.Equal(3_344, json.RootElement.GetProperty("TrainSteps").GetInt64());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deepbrain-checkpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeBackend : IBrainMlBackend
    {
        public FakeBackend(string kind, long trainSteps)
        {
            Kind = kind;
            TrainSteps = trainSteps;
        }

        public string? LoadedPath { get; private set; }
        public string? SavedPath { get; private set; }
        public bool IsAvailable => true;
        public string Kind { get; }
        public bool IsConnected => true;
        public string? LastError => null;
        public double LastRttMs => 0;
        public int InputDim => StateVectorizer.InputDim;
        public int ActionCount => ActionCatalog.Count;
        public int BufferSize => 0;
        public int BufferCapacity => 20_000;
        public double LastLoss => 0;
        public double AvgLoss100 => 0;
        public long TrainSteps { get; }
        public double AvgQ => 0;

        public Task<MlInferResult> InferAsync(float[] stateVec, float[] actionMask, MlConfig config, CancellationToken ct)
        {
            var probabilities = Enumerable.Repeat(1f / ActionCatalog.Count, ActionCatalog.Count).ToArray();
            return Task.FromResult(new MlInferResult(
                true,
                new double[ActionCatalog.Count],
                probabilities,
                0,
                0));
        }

        public Task<MlTrainResult> TrainAsync(Transition transition, MlConfig config, long tick, CancellationToken ct)
            => Task.FromResult(new MlTrainResult(false, 0, 0, TrainSteps, false));

        public bool TryLoad(string path, out int episodeId)
        {
            LoadedPath = path;
            episodeId = 0;
            return true;
        }

        public void TrySave(string path, int episodeId)
        {
            SavedPath = path;
        }

        public void Reset(MlConfig config) { }
        public void ResetCounters() { }
        public void UpdateConfig(MlConfig config) { }
        public bool TryConnect() => true;
        public void Disconnect() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
