using System.Diagnostics;
using System.Text;
using System.Collections.Concurrent;

namespace Queryeasy.Proxy;

internal sealed class SessionPerformanceTracker : IPerformanceRecorder
{
    private readonly ProxyPerformanceMetrics _global;
    private readonly long[] _counts;
    private readonly long[] _totalTicks;
    private readonly long[] _maxTicks;
    private readonly RequestTraceCollector _traceCollector;
    private readonly ConcurrentQueue<RequestTrace> _pendingS2cTraces = new();

    private RequestTrace? _currentRequest;
    private int _requestCounter;

    internal SessionPerformanceTracker(ProxyPerformanceMetrics global, int traceBufferCapacity = 1000)
    {
        _global = global;
        var length = Enum.GetValues<ProxyPerformanceStage>().Length;
        _counts = new long[length];
        _totalTicks = new long[length];
        _maxTicks = new long[length];
        _traceCollector = new RequestTraceCollector(traceBufferCapacity);
    }

    public ConcurrentQueue<RequestTrace> PendingS2cTraces => _pendingS2cTraces;

    public PerfScope Measure(ProxyPerformanceStage stage) => new(this, stage);

    public void Record(ProxyPerformanceStage stage, long elapsedTicks, long startTimestamp)
    {
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        var index = (int)stage;
        _counts[index]++;
        _totalTicks[index] += elapsedTicks;

        if (elapsedTicks > _maxTicks[index])
        {
            _maxTicks[index] = elapsedTicks;
        }

        // Push to global immediately so that periodic summary has data even for open sessions
        _global.Record(stage, elapsedTicks);

        _currentRequest?.RecordStage(stage, startTimestamp, startTimestamp + elapsedTicks);
    }

    public void BeginRequest(RequestType type, string? sqlPreview)
    {
        var requestId = Interlocked.Increment(ref _requestCounter);
        _currentRequest = new RequestTrace(requestId, type, Stopwatch.GetTimestamp())
        {
            SqlPreview = sqlPreview
        };
    }

    public void EndRequest(long writeCompleteTimestamp)
    {
        if (_currentRequest is null)
        {
            return;
        }

        _currentRequest.C2sWriteCompleteTimestamp = writeCompleteTimestamp;
        _pendingS2cTraces.Enqueue(_currentRequest);
        _currentRequest = null;
    }

    public void CompleteS2cTrace(RequestTrace trace, long readTimestamp, long writeTimestamp)
    {
        trace.S2cReadStartTimestamp = readTimestamp;
        trace.S2cWriteCompleteTimestamp = writeTimestamp;
        _traceCollector.Record(trace);
    }

    internal void MergeInto(ProxyPerformanceMetrics global)
    {
        // Stage stats are already pushed to global in Record() — no double-counting needed.
        // Only merge request traces for per-request waterfall analysis.
        _traceCollector.MergeInto(global);
    }

    public string BuildSessionSummary()
    {
        var builder = new StringBuilder("perf session");

        for (var index = 0; index < _counts.Length; index++)
        {
            if (_counts[index] == 0)
            {
                continue;
            }

            builder.Append(' ')
                .Append((ProxyPerformanceStage)index)
                .Append('=')
                .Append(FormatMilliseconds(_totalTicks[index]))
                .Append("ms");
        }

        return builder.Length == 13 ? string.Empty : builder.ToString();
    }

    public IReadOnlyList<RequestTrace> GetRequestTraces()
    {
        return _traceCollector.GetTraces();
    }

    public void Complete()
    {
        MergeInto(_global);
    }

    private static long FormatMilliseconds(long ticks)
    {
        if (ticks <= 0)
        {
            return 0;
        }

        return (long)(ticks * 1000.0 / Stopwatch.Frequency);
    }
}
