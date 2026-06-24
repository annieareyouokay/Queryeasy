using System.Diagnostics;
using System.Text;

namespace Queryeasy.Proxy;

internal sealed class ProxyPerformanceMetrics
{
    private readonly StagePercentileTracker[] _stages = CreateStageArray();

    public SessionPerformanceTracker CreateSessionTracker(int traceBufferCapacity = 1000) => new(this, traceBufferCapacity);

    public void Record(ProxyPerformanceStage stage, long elapsedTicks)
    {
        _stages[(int)stage].Record(elapsedTicks);
    }

    public StagePercentileSnapshot GetSnapshot(ProxyPerformanceStage stage)
    {
        return _stages[(int)stage].Snapshot();
    }

    public string BuildHumanSummary()
    {
        return PerformanceReportFormatter.BuildHumanSummary(this);
    }

    public string BuildJsonSummary()
    {
        return PerformanceReportFormatter.BuildJsonSummary(this);
    }

    public void ResetAll()
    {
        for (var index = 0; index < _stages.Length; index++)
        {
            _stages[index].Reset();
        }
    }

    internal StagePercentileTracker[] Stages => _stages;

    private static StagePercentileTracker[] CreateStageArray()
    {
        var stages = new StagePercentileTracker[Enum.GetValues<ProxyPerformanceStage>().Length];
        for (var index = 0; index < stages.Length; index++)
        {
            stages[index] = new StagePercentileTracker();
        }

        return stages;
    }
}
