using System.Diagnostics;
using System.Text;

namespace Queryeasy.Proxy;

internal sealed class SessionPerformanceTracker : IPerformanceRecorder
{
    private readonly ProxyPerformanceMetrics _global;
    private readonly long[] _counts;
    private readonly long[] _totalTicks;
    private readonly long[] _maxTicks;

    internal SessionPerformanceTracker(ProxyPerformanceMetrics global)
    {
        _global = global;
        var length = Enum.GetValues<ProxyPerformanceStage>().Length;
        _counts = new long[length];
        _totalTicks = new long[length];
        _maxTicks = new long[length];
    }

    public PerfScope Measure(ProxyPerformanceStage stage) => new(this, stage);

    public void Record(ProxyPerformanceStage stage, long elapsedTicks)
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
    }

    internal void MergeInto(ProxyPerformanceMetrics global)
    {
        var stages = global.Stages;

        for (var index = 0; index < _counts.Length; index++)
        {
            if (_counts[index] == 0)
            {
                continue;
            }

            stages[index].MergeFrom(new ProxyPerformanceMetrics.StageSnapshot(
                _counts[index],
                _totalTicks[index],
                _maxTicks[index]));
        }
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
