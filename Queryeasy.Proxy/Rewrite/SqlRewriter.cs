using System.Text.RegularExpressions;

namespace Queryeasy.Proxy.Rewrite;

internal sealed class SqlRewriter
{
    private readonly IReadOnlyList<SqlRewriteRule> _rules;

    public SqlRewriter(IEnumerable<SqlRewriteRule> rules)
    {
        _rules = rules.Where(rule => rule.Enabled).ToArray();
    }

    public RewriteResult Rewrite(string sql)
    {
        return Rewrite(sql, QueryRewriteScope.SqlBatch, []);
    }

    public RewriteResult Rewrite(
        string sql,
        QueryRewriteScope scope,
        IReadOnlyList<RewriteParameterInfo> parameters)
    {
        var rewrittenSql = sql;
        var matchedRules = new List<string>();
        var parameterChanges = new List<RewriteParameterChange>();

        foreach (var rule in _rules)
        {
            if (!IsScopeMatch(rule, scope))
            {
                continue;
            }

            var conditionMatch = EvaluateCondition(rule, rewrittenSql, parameters);
            if (!conditionMatch.IsMatch)
            {
                continue;
            }

            try
            {
                var ruleChanged = false;
                var actions = GetActions(rule);

                foreach (var action in actions)
                {
                    switch (action.Type)
                    {
                        case SqlRewriteActionType.ReplaceSql:
                            if (string.IsNullOrEmpty(action.Find))
                            {
                                break;
                            }

                            var nextSql = ApplySqlAction(rewrittenSql, action);

                            if (!string.Equals(rewrittenSql, nextSql, StringComparison.Ordinal))
                            {
                                rewrittenSql = nextSql;
                                ruleChanged = true;
                            }

                            break;

                        case SqlRewriteActionType.SetParameterValue:
                            if (action.Value is null)
                            {
                                break;
                            }

                            foreach (var targetName in GetParameterActionTargets(action, conditionMatch.MatchedParameters))
                            {
                                parameterChanges.Add(new RewriteParameterChange(
                                    targetName,
                                    action.Value,
                                    null,
                                    rule.Name));
                                ruleChanged = true;
                            }

                            break;

                        case SqlRewriteActionType.SetParameterType:
                            if (string.IsNullOrWhiteSpace(action.SqlType))
                            {
                                var targetLabel = string.IsNullOrWhiteSpace(action.Name)
                                    ? "matched parameters"
                                    : action.Name;

                                return RewriteResult.Failed(
                                    sql,
                                    $"Rule '{rule.Name}' action SetParameterType for '{targetLabel}' requires SqlType.");
                            }

                            foreach (var targetName in GetParameterActionTargets(action, conditionMatch.MatchedParameters))
                            {
                                parameterChanges.Add(new RewriteParameterChange(
                                    targetName,
                                    null,
                                    action.SqlType,
                                    rule.Name));
                                ruleChanged = true;
                            }

                            break;
                    }
                }

                if (ruleChanged)
                {
                    matchedRules.Add(rule.Name);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                return RewriteResult.Failed(sql, $"Rule '{rule.Name}' failed: {ex.Message}");
            }
        }

        return matchedRules.Count > 0 || parameterChanges.Count > 0
            ? RewriteResult.ChangedBy(rewrittenSql, matchedRules, parameterChanges)
            : RewriteResult.Unchanged(sql);
    }

    private static bool IsScopeMatch(SqlRewriteRule rule, QueryRewriteScope scope)
    {
        return rule.Scope is QueryRewriteScope.Any || rule.Scope == scope;
    }

    private static ConditionMatchResult EvaluateCondition(
        SqlRewriteRule rule,
        string sql,
        IReadOnlyList<RewriteParameterInfo> parameters)
    {
        var condition = rule.When;
        var comparison = condition.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.IsNullOrEmpty(condition.SqlContains)
            && !sql.Contains(condition.SqlContains, comparison))
        {
            return ConditionMatchResult.NoMatch;
        }

        if (!string.IsNullOrEmpty(condition.SqlRegex))
        {
            var options = RegexOptions.CultureInvariant;

            if (condition.IgnoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            if (!Regex.IsMatch(sql, condition.SqlRegex, options, TimeSpan.FromSeconds(1)))
            {
                return ConditionMatchResult.NoMatch;
            }
        }

        var hasParameterExists = !string.IsNullOrEmpty(condition.ParameterExists);
        var hasParameterNameRegex = !string.IsNullOrEmpty(condition.ParameterNameRegex);
        var hasParameterType = !string.IsNullOrEmpty(condition.ParameterType);
        var hasParameterFilter = hasParameterExists || hasParameterNameRegex || hasParameterType;

        if (!hasParameterFilter)
        {
            return ConditionMatchResult.Match([]);
        }

        var matchedParameters = parameters
            .Where(parameter => MatchesParameterNameFilter(condition, parameter))
            .Where(parameter => !hasParameterType || IsTypeMatch(parameter.TypeName, condition.ParameterType!))
            .ToArray();

        return matchedParameters.Length > 0
            ? ConditionMatchResult.Match(matchedParameters)
            : ConditionMatchResult.NoMatch;
    }

    private static IReadOnlyList<SqlRewriteAction> GetActions(SqlRewriteRule rule)
    {
        if (rule.Actions.Count > 0)
        {
            return rule.Actions;
        }

        if (string.IsNullOrEmpty(rule.Find))
        {
            return [];
        }

        return
        [
            new SqlRewriteAction
            {
                Type = SqlRewriteActionType.ReplaceSql,
                MatchType = rule.MatchType,
                Find = rule.Find,
                Replace = rule.Replace,
                IgnoreCase = rule.IgnoreCase
            }
        ];
    }

    private static string ApplySqlAction(string sql, SqlRewriteAction action)
    {
        return action.MatchType switch
        {
            SqlRewriteMatchType.Contains => ApplyContainsRule(sql, action),
            SqlRewriteMatchType.Regex => ApplyRegexRule(sql, action),
            _ => sql
        };
    }

    private static string ApplyContainsRule(string sql, SqlRewriteAction action)
    {
        var comparison = action.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return sql.Replace(action.Find, action.Replace, comparison);
    }

    private static string ApplyRegexRule(string sql, SqlRewriteAction action)
    {
        var options = RegexOptions.CultureInvariant;

        if (action.IgnoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return Regex.Replace(sql, action.Find, action.Replace, options, TimeSpan.FromSeconds(1));
    }

    private static bool MatchesParameterNameFilter(SqlRewriteCondition condition, RewriteParameterInfo parameter)
    {
        var hasParameterExists = !string.IsNullOrEmpty(condition.ParameterExists);
        var hasParameterNameRegex = !string.IsNullOrEmpty(condition.ParameterNameRegex);

        if (!hasParameterExists && !hasParameterNameRegex)
        {
            return true;
        }

        if (hasParameterExists && IsParameterNameMatch(parameter.Name, condition.ParameterExists!))
        {
            return true;
        }

        if (hasParameterNameRegex)
        {
            var options = RegexOptions.CultureInvariant;

            if (condition.IgnoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            return Regex.IsMatch(parameter.Name, condition.ParameterNameRegex!, options, TimeSpan.FromSeconds(1));
        }

        return false;
    }

    private static IReadOnlyList<string> GetParameterActionTargets(
        SqlRewriteAction action,
        IReadOnlyList<RewriteParameterInfo> matchedParameters)
    {
        if (!string.IsNullOrWhiteSpace(action.Name))
        {
            return [ParameterNameHelper.EnsureAtPrefix(action.Name)];
        }

        return matchedParameters
            .Select(parameter => ParameterNameHelper.EnsureAtPrefix(parameter.Name))
            .ToArray();
    }

    private static bool IsParameterNameMatch(string actual, string expected)
    {
        return ParameterNameHelper.Equals(actual, expected);
    }

    private static bool IsTypeMatch(string actualTypeName, string expectedTypeName)
    {
        return string.Equals(
            actualTypeName.Trim(),
            expectedTypeName.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ConditionMatchResult(bool IsMatch, IReadOnlyList<RewriteParameterInfo> MatchedParameters)
    {
        public static ConditionMatchResult Match(IReadOnlyList<RewriteParameterInfo> matchedParameters)
        {
            return new ConditionMatchResult(true, matchedParameters);
        }

        public static ConditionMatchResult NoMatch { get; } = new(false, []);
    }
}
