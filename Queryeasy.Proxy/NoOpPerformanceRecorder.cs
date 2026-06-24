namespace Queryeasy.Proxy;

internal sealed class NoOpPerformanceRecorder : IPerformanceRecorder
{
    public static NoOpPerformanceRecorder Instance { get; } = new();

    public PerfScope Measure(ProxyPerformanceStage stage) => new(null, stage);

    public void Record(ProxyPerformanceStage stage, long elapsedTicks, long startTimestamp)
    {
    }

    public void BeginRequest(RequestType type, string? sqlPreview)
    {
    }

    public void EndRequest(long writeCompleteTimestamp)
    {
    }
}
