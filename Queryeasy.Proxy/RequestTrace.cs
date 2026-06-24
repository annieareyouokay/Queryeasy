using System.Diagnostics;
using System.Text;

namespace Queryeasy.Proxy;

internal enum RequestType
{
    SqlBatch,
    RpcRequest,
    RawTls
}

internal readonly record struct StageRecord(
    ProxyPerformanceStage Stage,
    long StartTimestamp,
    long EndTimestamp);

internal sealed class RequestTrace
{
    private readonly List<StageRecord> _stages = new();

    public int RequestId { get; }
    public RequestType Type { get; }
    public string? SqlPreview { get; set; }
    public long RequestStartTimestamp { get; }
    public long C2sWriteCompleteTimestamp { get; set; }
    public long S2cReadStartTimestamp { get; set; }
    public long S2cWriteCompleteTimestamp { get; set; }
    public bool RewriteMatched { get; set; }
    public string? RewriteRuleName { get; set; }
    public IReadOnlyList<StageRecord> Stages => _stages;

    public RequestTrace(int requestId, RequestType type, long requestStartTimestamp)
    {
        RequestId = requestId;
        Type = type;
        RequestStartTimestamp = requestStartTimestamp;
    }

    public void RecordStage(ProxyPerformanceStage stage, long startTimestamp, long endTimestamp)
    {
        _stages.Add(new StageRecord(stage, startTimestamp, endTimestamp));
    }

    public bool IsComplete => S2cWriteCompleteTimestamp > 0;

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    public double C2sTotalMs =>
        C2sWriteCompleteTimestamp > 0
            ? TicksToMs(C2sWriteCompleteTimestamp - RequestStartTimestamp)
            : 0;

    public double S2cWaitMs =>
        S2cReadStartTimestamp > 0 && C2sWriteCompleteTimestamp > 0
            ? TicksToMs(S2cReadStartTimestamp - C2sWriteCompleteTimestamp)
            : 0;

    public double S2cTotalMs =>
        S2cWriteCompleteTimestamp > 0 && S2cReadStartTimestamp > 0
            ? TicksToMs(S2cWriteCompleteTimestamp - S2cReadStartTimestamp)
            : 0;

    public double EndToEndMs =>
        S2cWriteCompleteTimestamp > 0
            ? TicksToMs(S2cWriteCompleteTimestamp - RequestStartTimestamp)
            : 0;

    public string BuildWaterfall()
    {
        if (_stages.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var e2eMs = EndToEndMs;
        var c2sMs = C2sTotalMs;

        sb.Append($"--- Req #{RequestId} [{Type}] {e2eMs:F1}ms end-to-end (c2s={c2sMs:F1}ms");

        if (S2cWaitMs > 0)
        {
            sb.Append($", s2cWait={S2cWaitMs:F1}ms");
        }

        if (S2cTotalMs > 0)
        {
            sb.Append($", s2c={S2cTotalMs:F1}ms");
        }

        sb.AppendLine(") ---");

        foreach (var stage in _stages)
        {
            var offsetMs = TicksToMs(stage.StartTimestamp - RequestStartTimestamp);
            var durationMs = TicksToMs(stage.EndTimestamp - stage.StartTimestamp);
            var stageName = FormatStageName(stage.Stage);
            var marker = IsSlowStage(durationMs) ? "  ***" : string.Empty;
            sb.AppendLine($"  +{offsetMs,7:F1}ms  {stageName,-30} {durationMs,7:F3}ms{marker}");
        }

        if (C2sWriteCompleteTimestamp > 0)
        {
            var sentOffsetMs = TicksToMs(C2sWriteCompleteTimestamp - RequestStartTimestamp);
            sb.AppendLine($"  +{sentOffsetMs,7:F1}ms  >>> sent to SQL Server");
        }

        if (S2cReadStartTimestamp > 0)
        {
            var s2cOffsetMs = TicksToMs(S2cReadStartTimestamp - RequestStartTimestamp);
            sb.AppendLine($"  +{s2cOffsetMs,7:F1}ms  <<< response from SQL Server");
        }

        if (SqlPreview is not null)
        {
            var preview = SqlPreview.Length > 200 ? SqlPreview[..200] + "..." : SqlPreview;
            sb.AppendLine($"  SQL: {preview}");
        }

        return sb.ToString();
    }

    private static bool IsSlowStage(double durationMs)
    {
        return durationMs > 10;
    }

    private static string FormatStageName(ProxyPerformanceStage stage)
    {
        return stage switch
        {
            ProxyPerformanceStage.C2sReadMessage => "c2s:readMessage",
            ProxyPerformanceStage.C2sWritePackets => "c2s:writePackets",
            ProxyPerformanceStage.C2sSqlBatchDecode => "c2s:sqlBatchDecode",
            ProxyPerformanceStage.C2sSqlBatchRewrite => "c2s:sqlBatchRewrite",
            ProxyPerformanceStage.C2sSqlBatchEncodeSplit => "c2s:encodeSplit",
            ProxyPerformanceStage.C2sRpcInspect => "c2s:rpcInspect",
            ProxyPerformanceStage.C2sRpcRewrite => "c2s:rpcRewrite",
            ProxyPerformanceStage.C2sRpcEncodeSplit => "c2s:rpcEncodeSplit",
            ProxyPerformanceStage.C2sRawTlsForward => "c2s:rawTlsForward",
            ProxyPerformanceStage.S2cRead => "s2c:read",
            ProxyPerformanceStage.S2cWrite => "s2c:write",
            _ => stage.ToString()
        };
    }
}
