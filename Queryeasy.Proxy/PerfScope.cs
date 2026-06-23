using System.Diagnostics;

namespace Queryeasy.Proxy;

internal readonly struct PerfScope : IDisposable
{
    private readonly IPerformanceRecorder? _recorder;
    private readonly ProxyPerformanceStage _stage;
    private readonly long _startTimestamp;

    public PerfScope(IPerformanceRecorder? recorder, ProxyPerformanceStage stage)
    {
        _recorder = recorder;
        _stage = stage;
        _startTimestamp = recorder is null ? 0 : Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        if (_recorder is null)
        {
            return;
        }

        var elapsedTicks = Stopwatch.GetTimestamp() - _startTimestamp;
        _recorder.Record(_stage, elapsedTicks);
    }
}
