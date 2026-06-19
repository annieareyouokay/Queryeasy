namespace Queryeasy.Proxy.Rewrite;

internal sealed class SqlRewriteRule
{
    public string Name { get; init; } = "Unnamed";

    public bool Enabled { get; init; } = true;

    public QueryRewriteScope Scope { get; init; } = QueryRewriteScope.Any;

    public SqlRewriteCondition When { get; init; } = new();

    public SqlRewriteMatchType MatchType { get; init; } = SqlRewriteMatchType.Contains;

    public string Find { get; init; } = string.Empty;

    public string Replace { get; init; } = string.Empty;

    public bool IgnoreCase { get; init; } = true;

    public List<SqlRewriteAction> Actions { get; init; } = [];
}
