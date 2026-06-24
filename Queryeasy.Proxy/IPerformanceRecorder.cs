namespace Queryeasy.Proxy;

internal interface IPerformanceRecorder
{
    PerfScope Measure(ProxyPerformanceStage stage);

    void Record(ProxyPerformanceStage stage, long elapsedTicks, long startTimestamp);

    void BeginRequest(RequestType type, string? sqlPreview);

    void EndRequest(long writeCompleteTimestamp);
}
