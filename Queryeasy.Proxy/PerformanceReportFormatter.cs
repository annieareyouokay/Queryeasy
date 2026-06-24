using System.Globalization;
using System.Text;

namespace Queryeasy.Proxy;

internal static class PerformanceReportFormatter
{
    private static readonly CultureInfo JsonCulture = CultureInfo.InvariantCulture;

    public static string BuildHumanSummary(ProxyPerformanceMetrics metrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Performance Summary ===");

        var c2sStages = new (ProxyPerformanceStage Stage, string Label)[]
        {
            (ProxyPerformanceStage.C2sReadMessage, "C2sReadMessage"),
            (ProxyPerformanceStage.C2sSqlBatchDecode, "C2sSqlBatchDecode"),
            (ProxyPerformanceStage.C2sSqlBatchRewrite, "C2sSqlBatchRewrite"),
            (ProxyPerformanceStage.C2sSqlBatchEncodeSplit, "C2sSqlBatchEncodeSplit"),
            (ProxyPerformanceStage.C2sRpcInspect, "C2sRpcInspect"),
            (ProxyPerformanceStage.C2sRpcRewrite, "C2sRpcRewrite"),
            (ProxyPerformanceStage.C2sRpcEncodeSplit, "C2sRpcEncodeSplit"),
            (ProxyPerformanceStage.C2sWritePackets, "C2sWritePackets"),
            (ProxyPerformanceStage.C2sRawTlsForward, "C2sRawTlsForward"),
        };

        var s2cStages = new (ProxyPerformanceStage Stage, string Label)[]
        {
            (ProxyPerformanceStage.S2cRead, "S2cRead"),
            (ProxyPerformanceStage.S2cWrite, "S2cWrite"),
        };

        var sessionStages = new (ProxyPerformanceStage Stage, string Label)[]
        {
            (ProxyPerformanceStage.SessionConnect, "SessionConnect"),
            (ProxyPerformanceStage.SessionPreLogin, "SessionPreLogin"),
            (ProxyPerformanceStage.SessionClientToServer, "SessionClientToServer"),
            (ProxyPerformanceStage.SessionServerToClient, "SessionServerToClient"),
        };

        AppendStageGroup(sb, "Session stages", sessionStages, metrics);
        AppendStageGroup(sb, "Client -> SQL Server (C2S) stages", c2sStages, metrics);
        AppendStageGroup(sb, "SQL Server -> Client (S2C) stages", s2cStages, metrics);

        return sb.ToString();
    }

    private static void AppendStageGroup(
        StringBuilder sb,
        string heading,
        (ProxyPerformanceStage Stage, string Label)[] stages,
        ProxyPerformanceMetrics metrics)
    {
        var hasData = false;
        foreach (var (stage, _) in stages)
        {
            if (metrics.GetSnapshot(stage) is { Count: > 0 })
            {
                hasData = true;
                break;
            }
        }

        if (!hasData)
        {
            return;
        }

        sb.AppendLine($"  {heading}:");

        foreach (var (stage, label) in stages)
        {
            var snap = metrics.GetSnapshot(stage);
            if (snap.Count == 0)
            {
                continue;
            }

            sb.Append($"    {label,-30}");
            sb.Append($" n={snap.Count,-8}");
            sb.AppendFormat(JsonCulture, " avg={0,8:F2}ms p50={1,8:F2}ms p95={2,8:F2}ms p99={3,8:F2}ms max={4,8:F2}ms",
                snap.AvgMs, snap.P50Ms, snap.P95Ms, snap.P99Ms, snap.MaxMs);
            sb.AppendLine();
        }
    }

    public static string BuildJsonSummary(ProxyPerformanceMetrics metrics)
    {
        var sb = new StringBuilder();
        sb.Append("{\"ts\":\"");
        sb.Append(DateTime.UtcNow.ToString("O"));
        sb.Append("\",\"stages\":{");

        var allStages = Enum.GetValues<ProxyPerformanceStage>();
        var first = true;

        foreach (var stage in allStages)
        {
            var snap = metrics.GetSnapshot(stage);
            if (snap.Count == 0)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append('"');
            sb.Append(stage);
            sb.Append("\":{");
            sb.Append("\"count\":").Append(snap.Count).Append(',');
            sb.Append("\"avgMs\":").Append(snap.AvgMs.ToString("F3", JsonCulture)).Append(',');
            sb.Append("\"p50Ms\":").Append(snap.P50Ms.ToString("F3", JsonCulture)).Append(',');
            sb.Append("\"p95Ms\":").Append(snap.P95Ms.ToString("F3", JsonCulture)).Append(',');
            sb.Append("\"p99Ms\":").Append(snap.P99Ms.ToString("F3", JsonCulture)).Append(',');
            sb.Append("\"maxMs\":").Append(snap.MaxMs.ToString("F3", JsonCulture));
            sb.Append('}');
        }

        sb.Append("}}");
        return sb.ToString();
    }
}
