namespace Queryeasy.Proxy;

internal sealed class ProxyMetrics
{
    private long _activeSessions;
    private long _acceptedSessions;
    private long _rejectedSessions;
    private long _clientToSqlBytes;
    private long _sqlToClientBytes;
    private long _sqlBatchCount;
    private long _rpcRequestCount;
    private long _rewriteMatched;
    private long _rewriteApplied;
    private long _rewriteFailed;
    private long _encodeFailed;
    private long _parseWarnings;
    private long _rawTlsFallbacks;
    private long _oversizedMessages;

    public void SessionAccepted()
    {
        Interlocked.Increment(ref _acceptedSessions);
        Interlocked.Increment(ref _activeSessions);
    }

    public void SessionRejected()
    {
        Interlocked.Increment(ref _rejectedSessions);
    }

    public void SessionClosed()
    {
        Interlocked.Decrement(ref _activeSessions);
    }

    public void AddClientToSqlBytes(long bytes)
    {
        Interlocked.Add(ref _clientToSqlBytes, bytes);
    }

    public void AddSqlToClientBytes(long bytes)
    {
        Interlocked.Add(ref _sqlToClientBytes, bytes);
    }

    public void SqlBatchInspected()
    {
        Interlocked.Increment(ref _sqlBatchCount);
    }

    public void RpcInspected()
    {
        Interlocked.Increment(ref _rpcRequestCount);
    }

    public void RewriteMatched()
    {
        Interlocked.Increment(ref _rewriteMatched);
    }

    public void RewriteApplied()
    {
        Interlocked.Increment(ref _rewriteApplied);
    }

    public void RewriteFailed()
    {
        Interlocked.Increment(ref _rewriteFailed);
    }

    public void EncodeFailed()
    {
        Interlocked.Increment(ref _encodeFailed);
    }

    public void ParseWarning()
    {
        Interlocked.Increment(ref _parseWarnings);
    }

    public void RawTlsFallback()
    {
        Interlocked.Increment(ref _rawTlsFallbacks);
    }

    public void OversizedMessage()
    {
        Interlocked.Increment(ref _oversizedMessages);
    }

    public string BuildSummary()
    {
        return "metrics "
            + $"active_sessions={Read(ref _activeSessions)} "
            + $"accepted_sessions={Read(ref _acceptedSessions)} "
            + $"rejected_sessions={Read(ref _rejectedSessions)} "
            + $"client_to_sql_bytes={Read(ref _clientToSqlBytes)} "
            + $"sql_to_client_bytes={Read(ref _sqlToClientBytes)} "
            + $"sql_batches={Read(ref _sqlBatchCount)} "
            + $"rpc_requests={Read(ref _rpcRequestCount)} "
            + $"rewrite_matched={Read(ref _rewriteMatched)} "
            + $"rewrite_applied={Read(ref _rewriteApplied)} "
            + $"rewrite_failed={Read(ref _rewriteFailed)} "
            + $"encode_failed={Read(ref _encodeFailed)} "
            + $"parse_warnings={Read(ref _parseWarnings)} "
            + $"raw_tls_fallbacks={Read(ref _rawTlsFallbacks)} "
            + $"oversized_messages={Read(ref _oversizedMessages)}";
    }

    private static long Read(ref long value)
    {
        return Interlocked.Read(ref value);
    }
}
