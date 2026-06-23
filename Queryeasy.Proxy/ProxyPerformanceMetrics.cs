using System.Diagnostics;
using System.Text;

namespace Queryeasy.Proxy;

internal sealed class ProxyPerformanceMetrics
{
    private readonly StageStats[] _stages = CreateStageArray();

    public SessionPerformanceTracker CreateSessionTracker() => new(this);

    public void Record(ProxyPerformanceStage stage, long elapsedTicks)
    {
        _stages[(int)stage].Record(elapsedTicks);
    }

    public string BuildSummary()
    {
        var builder = new StringBuilder("perf");

        for (var index = 0; index < _stages.Length; index++)
        {
            var snapshot = _stages[index].Snapshot();
            if (snapshot.Count == 0)
            {
                continue;
            }

            builder.Append(' ')
                .Append(FormatStageName((ProxyPerformanceStage)index))
                .Append('=')
                .Append(snapshot.Count)
                .Append('/')
                .Append(FormatMilliseconds(snapshot.TotalTicks))
                .Append("ms/avg")
                .Append(FormatMilliseconds(snapshot.TotalTicks / snapshot.Count))
                .Append("/max")
                .Append(FormatMilliseconds(snapshot.MaxTicks));
        }

        return builder.Length == 4 ? "perf" : builder.ToString();
    }

    internal StageStats[] Stages => _stages;

    private static StageStats[] CreateStageArray()
    {
        var stages = new StageStats[Enum.GetValues<ProxyPerformanceStage>().Length];
        for (var index = 0; index < stages.Length; index++)
        {
            stages[index] = new StageStats();
        }

        return stages;
    }

    private static string FormatStageName(ProxyPerformanceStage stage)
    {
        return stage.ToString();
    }

    private static long FormatMilliseconds(long ticks)
    {
        if (ticks <= 0)
        {
            return 0;
        }

        return (long)(ticks * 1000.0 / Stopwatch.Frequency);
    }

    internal sealed class StageStats
    {
        private long _count;
        private long _totalTicks;
        private long _maxTicks;

        public void Record(long elapsedTicks)
        {
            if (elapsedTicks < 0)
            {
                elapsedTicks = 0;
            }

            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _totalTicks, elapsedTicks);

            UpdateMax(elapsedTicks);
        }

        public void MergeFrom(StageSnapshot snapshot)
        {
            if (snapshot.Count == 0)
            {
                return;
            }

            Interlocked.Add(ref _count, snapshot.Count);
            Interlocked.Add(ref _totalTicks, snapshot.TotalTicks);
            UpdateMax(snapshot.MaxTicks);
        }

        public StageSnapshot Snapshot()
        {
            return new StageSnapshot(
                Interlocked.Read(ref _count),
                Interlocked.Read(ref _totalTicks),
                Interlocked.Read(ref _maxTicks));
        }

        private void UpdateMax(long elapsedTicks)
        {
            while (true)
            {
                var currentMax = Interlocked.Read(ref _maxTicks);
                if (elapsedTicks <= currentMax)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxTicks, elapsedTicks, currentMax) == currentMax)
                {
                    return;
                }
            }
        }
    }

    internal readonly record struct StageSnapshot(long Count, long TotalTicks, long MaxTicks);
}
