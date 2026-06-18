namespace Queryeasy.Proxy.Rewrite;

internal sealed class SqlRewriteRule
{
    public string Name { get; init; } = "Unnamed";

    public bool Enabled { get; init; } = true;

    public SqlRewriteMatchType MatchType { get; init; } = SqlRewriteMatchType.Contains;

    public string Find { get; init; } = string.Empty;

    public string Replace { get; init; } = string.Empty;

    public bool IgnoreCase { get; init; } = true;
}
