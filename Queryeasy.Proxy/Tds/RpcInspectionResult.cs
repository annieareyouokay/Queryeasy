namespace Queryeasy.Proxy.Tds;

internal sealed class RpcInspectionResult
{
    public RpcInspectionResult(
        bool containsSpExecuteSql,
        string procedureName,
        string? statement,
        string? parameterDeclaration,
        IReadOnlyList<RpcParameterInspectionResult> parameters,
        RpcSpExecuteSqlRequest? spExecuteSqlRequest,
        string? parseWarning)
    {
        ContainsSpExecuteSql = containsSpExecuteSql;
        ProcedureName = procedureName;
        Statement = statement;
        ParameterDeclaration = parameterDeclaration;
        Parameters = parameters;
        SpExecuteSqlRequest = spExecuteSqlRequest;
        ParseWarning = parseWarning;
    }

    public bool ContainsSpExecuteSql { get; }

    public string ProcedureName { get; }

    public string? Statement { get; }

    public string? ParameterDeclaration { get; }

    public IReadOnlyList<RpcParameterInspectionResult> Parameters { get; }

    public RpcSpExecuteSqlRequest? SpExecuteSqlRequest { get; }

    public string? ParseWarning { get; }
}

internal sealed class RpcParameterInspectionResult
{
    public RpcParameterInspectionResult(
        string name,
        string typeName,
        string? value,
        byte status,
        RpcParameterEncoding encoding)
    {
        Name = name;
        TypeName = typeName;
        Value = value;
        Status = status;
        Encoding = encoding;
    }

    public string Name { get; }

    public string TypeName { get; }

    public string? Value { get; }

    public byte Status { get; }

    public RpcParameterEncoding Encoding { get; }
}

internal sealed class RpcParameterEncoding
{
    public RpcParameterEncoding(
        int parameterStartOffset,
        int parameterEndOffset,
        int typeInfoStartOffset,
        int typeInfoEndOffset,
        int valueLengthOffset,
        int valueLengthSize,
        int valueOffset,
        int valueLength,
        byte typeId,
        int? maxLength,
        bool isUnicode,
        byte[]? collation)
    {
        ParameterStartOffset = parameterStartOffset;
        ParameterEndOffset = parameterEndOffset;
        TypeInfoStartOffset = typeInfoStartOffset;
        TypeInfoEndOffset = typeInfoEndOffset;
        ValueLengthOffset = valueLengthOffset;
        ValueLengthSize = valueLengthSize;
        ValueOffset = valueOffset;
        ValueLength = valueLength;
        TypeId = typeId;
        MaxLength = maxLength;
        IsUnicode = isUnicode;
        Collation = collation;
    }

    public int ParameterStartOffset { get; }

    public int ParameterEndOffset { get; }

    public int TypeInfoStartOffset { get; }

    public int TypeInfoEndOffset { get; }

    public int ValueLengthOffset { get; }

    public int ValueLengthSize { get; }

    public int ValueOffset { get; }

    public int ValueLength { get; }

    public byte TypeId { get; }

    public int? MaxLength { get; }

    public bool IsUnicode { get; }

    public byte[]? Collation { get; }
}
