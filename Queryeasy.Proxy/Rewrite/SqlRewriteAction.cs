namespace Queryeasy.Proxy.Rewrite;

internal sealed class SqlRewriteAction
{
    public SqlRewriteActionType Type { get; init; } = SqlRewriteActionType.ReplaceSql;

    public SqlRewriteMatchType MatchType { get; init; } = SqlRewriteMatchType.Contains;

    public string Find { get; init; } = string.Empty;

    public string Replace { get; init; } = string.Empty;

    public bool IgnoreCase { get; init; } = true;

    public string Name { get; init; } = string.Empty;

    public string? Value { get; init; }

    public string? SqlType { get; init; }
}
