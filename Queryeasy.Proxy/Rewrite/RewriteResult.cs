namespace Queryeasy.Proxy.Rewrite;

internal sealed class RewriteResult
{
    private RewriteResult(
        string sql,
        bool changed,
        IReadOnlyList<string> ruleNames,
        IReadOnlyList<RewriteParameterChange> parameterChanges,
        string? error)
    {
        Sql = sql;
        Changed = changed;
        RuleNames = ruleNames;
        ParameterChanges = parameterChanges;
        Error = error;
    }

    public string Sql { get; }

    public bool Changed { get; }

    public string? RuleName => RuleNames.Count > 0 ? RuleNames[^1] : null;

    public IReadOnlyList<string> RuleNames { get; }

    public IReadOnlyList<RewriteParameterChange> ParameterChanges { get; }

    public string? Error { get; }

    public static RewriteResult Unchanged(string sql)
    {
        return new RewriteResult(sql, false, [], [], null);
    }

    public static RewriteResult ChangedBy(string sql, string ruleName)
    {
        return new RewriteResult(sql, true, [ruleName], [], null);
    }

    public static RewriteResult ChangedBy(
        string sql,
        IReadOnlyList<string> ruleNames,
        IReadOnlyList<RewriteParameterChange> parameterChanges)
    {
        return new RewriteResult(sql, ruleNames.Count > 0 || parameterChanges.Count > 0, ruleNames, parameterChanges, null);
    }

    public static RewriteResult Failed(string originalSql, string error)
    {
        return new RewriteResult(originalSql, false, [], [], error);
    }
}
