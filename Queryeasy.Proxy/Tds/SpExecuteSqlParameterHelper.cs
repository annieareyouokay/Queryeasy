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
        var declaredNames = ParseDeclaredParameterNames(parameterDeclaration);

        if (declaredNames.Count == sqlParameters.Count)
        {
            return declaredNames;
        }

        return sqlParameters
            .Select(parameter => parameter.Name)
            .Where(name => !string.IsNullOrEmpty(name))
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
        if (string.IsNullOrWhiteSpace(parameterDeclaration))
        {
            return [];
        }

        return ParameterNameRegex
            .Matches(parameterDeclaration)
            .Select(match => $"@{match.Groups[1].Value}")
            .ToArray();
    }

}
