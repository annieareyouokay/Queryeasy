namespace Queryeasy.Proxy.Tds;

internal sealed class SqlBatchMessage
{
    public SqlBatchMessage(byte[] headerPrefix, string sql)
    {
        HeaderPrefix = headerPrefix;
        Sql = sql;
    }

    public byte[] HeaderPrefix { get; }

    public string Sql { get; }
}
