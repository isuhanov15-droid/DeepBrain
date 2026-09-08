using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record LifeStatsDto(
    [property: JsonPropertyName("anxiousScore")] double AnxiousScore,
    [property: JsonPropertyName("calmScore")] double CalmScore,
    [property: JsonPropertyName("curiousScore")] double CuriousScore,
    [property: JsonPropertyName("p95Pain")] double P95Pain
)
{
    [JsonPropertyName("neutralScore")] public double NeutralScore { get; init; }
    [JsonPropertyName("tenderScore")] public double TenderScore { get; init; }
    [JsonPropertyName("lowScore")] public double LowScore { get; init; }
    [JsonPropertyName("frustratedScore")] public double FrustratedScore { get; init; }
    [JsonPropertyName("otherScore")] public double OtherScore { get; init; }
}
