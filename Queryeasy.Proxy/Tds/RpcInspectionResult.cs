namespace Queryeasy.Proxy.Tds;

internal sealed class RpcInspectionResult
{
    public RpcInspectionResult(
        bool containsSpExecuteSql,
        string procedureName,
        string? statement,
        string? parameterDeclaration,
        IReadOnlyList<RpcParameterInspectionResult> parameters,
        string? parseWarning)
    {
        ContainsSpExecuteSql = containsSpExecuteSql;
        ProcedureName = procedureName;
        Statement = statement;
        ParameterDeclaration = parameterDeclaration;
        Parameters = parameters;
        ParseWarning = parseWarning;
    }

    public bool ContainsSpExecuteSql { get; }

    public string ProcedureName { get; }

    public string? Statement { get; }

    public string? ParameterDeclaration { get; }

    public IReadOnlyList<RpcParameterInspectionResult> Parameters { get; }

    public string? ParseWarning { get; }
}

internal sealed class RpcParameterInspectionResult
{
    public RpcParameterInspectionResult(string name, string typeName, string? value)
    {
        Name = name;
        TypeName = typeName;
        Value = value;
    }

    public string Name { get; }

    public string TypeName { get; }

    public string? Value { get; }
}
