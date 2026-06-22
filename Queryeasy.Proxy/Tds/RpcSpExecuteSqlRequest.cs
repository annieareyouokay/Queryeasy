namespace Queryeasy.Proxy.Tds;

internal sealed class RpcSpExecuteSqlRequest
{
    public RpcSpExecuteSqlRequest(
        byte[] payload,
        string statement,
        string? parameterDeclaration,
        IReadOnlyList<RpcParameterInspectionResult> parameters)
    {
        Payload = payload;
        Statement = statement;
        ParameterDeclaration = parameterDeclaration;
        Parameters = parameters;
    }

    public byte[] Payload { get; }

    public string Statement { get; }

    public string? ParameterDeclaration { get; }

    public IReadOnlyList<RpcParameterInspectionResult> Parameters { get; }

    public IReadOnlyList<RpcParameterInspectionResult> SqlParameters => Parameters.Count > 2
        ? Parameters.Skip(2).ToArray()
        : [];
}
