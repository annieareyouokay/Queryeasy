namespace Queryeasy.Proxy.Tds;

internal sealed class RpcInspectionResult
{
    public RpcInspectionResult(bool containsSpExecuteSql, string unicodePreview)
    {
        ContainsSpExecuteSql = containsSpExecuteSql;
        UnicodePreview = unicodePreview;
    }

    public bool ContainsSpExecuteSql { get; }

    public string UnicodePreview { get; }
}
