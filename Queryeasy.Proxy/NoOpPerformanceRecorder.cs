namespace Queryeasy.Proxy;

internal sealed class NoOpPerformanceRecorder : IPerformanceRecorder
{
    public static NoOpPerformanceRecorder Instance { get; } = new();

    public PerfScope Measure(ProxyPerformanceStage stage) => new(null, stage);

    public void Record(ProxyPerformanceStage stage, long elapsedTicks)
    {
    }
}
