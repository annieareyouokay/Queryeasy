using System.Diagnostics;

namespace Queryeasy.Proxy;

internal sealed class RequestTraceCollector
{
    private readonly RequestTrace?[] _buffer;
    private int _position;
    private int _count;
    private readonly int _capacity;

    public RequestTraceCollector(int capacity = 1000)
    {
        _capacity = capacity;
        _buffer = new RequestTrace[capacity];
    }

    public void Record(RequestTrace trace)
    {
        var idx = Interlocked.Increment(ref _position) - 1;
        var slot = idx % _capacity;
        _buffer[slot] = trace;

        if (idx >= _capacity)
        {
            Interlocked.Exchange(ref _count, _capacity);
        }
        else
        {
            Interlocked.Exchange(ref _count, idx + 1);
        }
    }

    public IReadOnlyList<RequestTrace> GetTraces()
    {
        var count = Interlocked.CompareExchange(ref _count, 0, 0);
        if (count == 0)
        {
            return Array.Empty<RequestTrace>();
        }

        var result = new List<RequestTrace>(count);
        var pos = Interlocked.CompareExchange(ref _position, 0, 0);

        for (var i = 0; i < count; i++)
        {
            var idx = (pos - i - 1) % _capacity;
            if (idx < 0)
            {
                idx += _capacity;
            }

            var trace = _buffer[idx];
            if (trace is not null)
            {
                result.Add(trace);
            }
        }

        return result;
    }

    public void Clear()
    {
        Array.Clear(_buffer);
        Interlocked.Exchange(ref _position, 0);
        Interlocked.Exchange(ref _count, 0);
    }

    public void MergeInto(ProxyPerformanceMetrics global)
    {
        var traces = GetTraces();
        foreach (var trace in traces)
        {
            foreach (var stage in trace.Stages)
            {
                var elapsedTicks = stage.EndTimestamp - stage.StartTimestamp;
                global.Record(stage.Stage, elapsedTicks);
            }
        }
    }
}
