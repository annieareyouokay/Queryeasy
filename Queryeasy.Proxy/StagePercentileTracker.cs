using System.Diagnostics;

namespace Queryeasy.Proxy;

internal sealed class StagePercentileTracker
{
    private long _count;
    private long _totalTicks;
    private long _maxTicks;
    private readonly long[] _samples;
    private long _sampleIndex;

    public StagePercentileTracker(int bufferCapacity = 10_000)
    {
        _samples = new long[bufferCapacity];
    }

    public void Record(long elapsedTicks)
    {
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _totalTicks, elapsedTicks);
        UpdateMax(elapsedTicks);

        var idx = Interlocked.Increment(ref _sampleIndex) - 1;
        if (idx < _samples.Length)
        {
            _samples[idx] = elapsedTicks;
        }
    }

    public void MergeFrom(StagePercentileSnapshot snapshot)
    {
        if (snapshot.Count == 0)
        {
            return;
        }

        Interlocked.Add(ref _count, snapshot.Count);
        Interlocked.Add(ref _totalTicks, (long)(snapshot.AvgMs * snapshot.Count * Stopwatch.Frequency / 1000.0));
        UpdateMax((long)(snapshot.MaxMs * Stopwatch.Frequency / 1000.0));
    }

    public StagePercentileSnapshot Snapshot()
    {
        var count = Interlocked.Read(ref _count);
        var totalTicks = Interlocked.Read(ref _totalTicks);
        var maxTicks = Interlocked.Read(ref _maxTicks);

        if (count == 0)
        {
            return new StagePercentileSnapshot(0, 0, 0, 0, 0, 0, 0);
        }

        var avgMs = TicksToMs(totalTicks / count);

        var sampleCount = Math.Min(count, _samples.Length);
        var sorted = _samples.AsSpan(0, (int)sampleCount).ToArray();
        Array.Sort(sorted);

        var p50Ms = Percentile(sorted, 50);
        var p95Ms = Percentile(sorted, 95);
        var p99Ms = Percentile(sorted, 99);
        var maxMs = TicksToMs(maxTicks);

        return new StagePercentileSnapshot(count, avgMs, p50Ms, p95Ms, p99Ms, maxMs, totalTicks);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _totalTicks, 0);
        Interlocked.Exchange(ref _maxTicks, 0);
        Interlocked.Exchange(ref _sampleIndex, 0);
        Array.Clear(_samples);
    }

    private static double Percentile(long[] sorted, int percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        index = Math.Clamp(index, 0, sorted.Length - 1);
        return TicksToMs(sorted[index]);
    }

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
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

internal readonly record struct StagePercentileSnapshot(
    long Count,
    double AvgMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    long TotalTicks);
