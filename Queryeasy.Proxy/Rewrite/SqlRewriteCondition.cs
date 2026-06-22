namespace Queryeasy.Proxy.Rewrite;

internal sealed class SqlRewriteCondition
{
    public string? SqlContains { get; init; }

    public string? SqlRegex { get; init; }

    public string? ParameterExists { get; init; }

    public bool IgnoreCase { get; init; } = true;
}
