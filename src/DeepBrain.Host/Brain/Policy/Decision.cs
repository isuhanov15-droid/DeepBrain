namespace DeepBrain.Host.Brain.Policy;

public sealed record Decision(
  string Name,
  float Confidence,
  string Reason
);

