using System.Text.RegularExpressions;
using Queryeasy.Proxy.Rewrite;

namespace Queryeasy.Proxy.Tds;

internal static class SpExecuteSqlParameterHelper
{
    private static readonly Regex ParameterNameRegex = new(
        @"@([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> GetLogicalParameterNames(
        string? parameterDeclaration,
        IReadOnlyList<RpcParameterInspectionResult> sqlParameters)
    {
        return GetLogicalParameters(parameterDeclaration, sqlParameters)
            .Select(parameter => parameter.Name)
            .ToArray();
    }

    public static IReadOnlyList<RewriteParameterInfo> GetLogicalParameters(
        string? parameterDeclaration,
        IReadOnlyList<RpcParameterInspectionResult> sqlParameters)
    {
        var declaredParameters = ParseDeclaredParameters(parameterDeclaration);

        if (declaredParameters.Count == sqlParameters.Count)
        {
            return declaredParameters
                .Select((entry, index) => new RewriteParameterInfo(
                    entry.Name,
                    ResolveTypeName(entry.TypeName, sqlParameters[index].TypeName)))
                .ToArray();
        }

        return sqlParameters
            .Where(parameter => !string.IsNullOrEmpty(parameter.Name))
            .Select(parameter => new RewriteParameterInfo(parameter.Name, parameter.TypeName))
            .ToArray();
    }

    public static RpcParameterInspectionResult? ResolveParameter(
        RpcSpExecuteSqlRequest request,
        string logicalName)
    {
        var normalized = ParameterNameHelper.Normalize(logicalName);
        var sqlParameters = request.SqlParameters;

        var directMatch = sqlParameters.FirstOrDefault(parameter =>
            string.Equals(ParameterNameHelper.Normalize(parameter.Name), normalized, StringComparison.OrdinalIgnoreCase));

        if (directMatch is not null)
        {
            return directMatch;
        }

        var declaredNames = ParseDeclaredParameterNames(request.ParameterDeclaration);
        for (var index = 0; index < declaredNames.Count; index++)
        {
            if (string.Equals(ParameterNameHelper.Normalize(declaredNames[index]), normalized, StringComparison.OrdinalIgnoreCase)
                && index < sqlParameters.Count)
            {
                return sqlParameters[index];
            }
        }

        return null;
    }

    public static IReadOnlyList<string> ParseDeclaredParameterNames(string? parameterDeclaration)
    {
        return ParseDeclaredParameters(parameterDeclaration)
            .Select(parameter => parameter.Name)
            .ToArray();
    }

    public static IReadOnlyList<(string Name, string TypeName)> ParseDeclaredParameters(string? parameterDeclaration)
    {
        if (string.IsNullOrWhiteSpace(parameterDeclaration))
        {
            return [];
        }

        var matches = ParameterNameRegex.Matches(parameterDeclaration);
        if (matches.Count == 0)
        {
            return [];
        }

        var results = new List<(string Name, string TypeName)>(matches.Count);

        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var name = $"@{match.Groups[1].Value}";
            var typeStart = match.Index + match.Length;
            var typeEnd = index + 1 < matches.Count
                ? matches[index + 1].Index
                : parameterDeclaration.Length;
            var typeName = parameterDeclaration[typeStart..typeEnd].Trim().TrimEnd(',');

            if (!string.IsNullOrWhiteSpace(typeName))
            {
                results.Add((name, typeName));
            }
        }

        return results;
    }

    private static string ResolveTypeName(string declaredTypeName, string fallbackTypeName)
    {
        return string.IsNullOrWhiteSpace(declaredTypeName) ? fallbackTypeName : declaredTypeName;
    }
}
