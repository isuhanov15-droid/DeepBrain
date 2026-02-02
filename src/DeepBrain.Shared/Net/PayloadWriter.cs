namespace DeepBrain.Shared.Net;

public static class PayloadWriter
{
    public static object? Write<T>(T value) => value;
}
