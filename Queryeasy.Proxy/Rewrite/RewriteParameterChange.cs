namespace Queryeasy.Proxy.Rewrite;

internal sealed class RewriteParameterChange
{
    public RewriteParameterChange(string name, string? value, string? sqlType, string ruleName)
    {
        Name = name;
        Value = value;
        SqlType = sqlType;
        RuleName = ruleName;
    }

    public string Name { get; }

    public string? Value { get; }

    public string? SqlType { get; }

    public string RuleName { get; }
}
