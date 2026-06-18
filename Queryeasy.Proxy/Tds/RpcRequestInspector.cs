using System.Text;

namespace Queryeasy.Proxy.Tds;

internal static class RpcRequestInspector
{
    private static readonly Encoding SqlEncoding = Encoding.Unicode;

    public static RpcInspectionResult Inspect(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return new RpcInspectionResult(false, string.Empty);
        }

        var evenLength = payload.Length - payload.Length % 2;
        var unicodePreview = SqlEncoding.GetString(payload, 0, evenLength);
        var containsSpExecuteSql = unicodePreview.Contains("sp_executesql", StringComparison.OrdinalIgnoreCase);

        return new RpcInspectionResult(containsSpExecuteSql, unicodePreview);
    }
}
