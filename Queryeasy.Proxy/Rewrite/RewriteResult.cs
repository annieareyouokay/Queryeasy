namespace Queryeasy.Proxy.Rewrite;

internal sealed class RewriteResult
{
    private RewriteResult(string sql, bool changed, string? ruleName, string? error)
    {
        Sql = sql;
        Changed = changed;
        RuleName = ruleName;
        Error = error;
    }

    public string Sql { get; }

    public bool Changed { get; }

    public string? RuleName { get; }

    public string? Error { get; }

    public static RewriteResult Unchanged(string sql)
    {
        return new RewriteResult(sql, false, null, null);
    }

    public static RewriteResult ChangedBy(string sql, string ruleName)
    {
        return new RewriteResult(sql, true, ruleName, null);
    }

    public static RewriteResult Failed(string originalSql, string error)
    {
        return new RewriteResult(originalSql, false, null, error);
    }
}
