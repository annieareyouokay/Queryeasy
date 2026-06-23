namespace Queryeasy.Proxy;

internal sealed record InspectionCapabilities(
    bool IsForwardOnly,
    bool InspectSqlBatch,
    bool InspectRpc,
    bool RewriteSqlBatch,
    bool RewriteRpc,
    bool LogSqlText);
