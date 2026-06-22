namespace Queryeasy.Proxy.Rewrite;

internal static class ParameterNameHelper
{
    public static string Normalize(string name)
    {
        return name.StartsWith('@') ? name[1..] : name;
    }

    public static string EnsureAtPrefix(string name)
    {
        return name.StartsWith('@') ? name : $"@{name}";
    }

    public static bool Equals(string actual, string expected)
    {
        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.OrdinalIgnoreCase);
    }
}
