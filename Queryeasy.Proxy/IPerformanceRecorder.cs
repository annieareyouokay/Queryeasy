namespace Queryeasy.Proxy;

internal interface IPerformanceRecorder
{
    PerfScope Measure(ProxyPerformanceStage stage);

    void Record(ProxyPerformanceStage stage, long elapsedTicks);
}
