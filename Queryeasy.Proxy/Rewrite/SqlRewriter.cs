using System.Text.RegularExpressions;

namespace Queryeasy.Proxy.Rewrite;

internal sealed class SqlRewriter
{
    private readonly IReadOnlyList<CompiledSqlRewriteRule> _rules;

    public SqlRewriter(IEnumerable<SqlRewriteRule> rules)
    {
        _rules = rules
            .Where(rule => rule.Enabled)
            .Select(CompiledSqlRewriteRule.Create)
            .ToArray();
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

        foreach (var compiledRule in _rules)
        {
            var rule = compiledRule.Rule;

            if (!IsScopeMatch(rule, scope))
            {
                continue;
            }

            var conditionMatch = EvaluateCondition(compiledRule, rewrittenSql, parameters);
            if (!conditionMatch.IsMatch)
            {
                continue;
            }

            try
            {
                var ruleChanged = false;

                foreach (var compiledAction in compiledRule.Actions)
                {
                    switch (compiledAction.Action.Type)
                    {
                        case SqlRewriteActionType.ReplaceSql:
                            if (string.IsNullOrEmpty(compiledAction.Action.Find))
                            {
                                break;
                            }

                            var nextSql = ApplySqlAction(rewrittenSql, compiledAction);

                            if (!string.Equals(rewrittenSql, nextSql, StringComparison.Ordinal))
                            {
                                rewrittenSql = nextSql;
                                ruleChanged = true;
                            }

                            break;

                        case SqlRewriteActionType.SetParameterValue:
                            if (compiledAction.Action.Value is null)
                            {
                                break;
                            }

                            foreach (var targetName in GetParameterActionTargets(compiledAction.Action, conditionMatch.MatchedParameters))
                            {
                                parameterChanges.Add(new RewriteParameterChange(
                                    targetName,
                                    compiledAction.Action.Value,
                                    null,
                                    rule.Name));
                                ruleChanged = true;
                            }

                            break;

                        case SqlRewriteActionType.SetParameterType:
                            if (string.IsNullOrWhiteSpace(compiledAction.Action.SqlType))
                            {
                                var targetLabel = string.IsNullOrWhiteSpace(compiledAction.Action.Name)
                                    ? "matched parameters"
                                    : compiledAction.Action.Name;

                                return RewriteResult.Failed(
                                    sql,
                                    $"Rule '{rule.Name}' action SetParameterType for '{targetLabel}' requires SqlType.");
                            }

                            foreach (var targetName in GetParameterActionTargets(compiledAction.Action, conditionMatch.MatchedParameters))
                            {
                                parameterChanges.Add(new RewriteParameterChange(
                                    targetName,
                                    null,
                                    compiledAction.Action.SqlType,
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
            catch (RegexMatchTimeoutException ex)
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
        CompiledSqlRewriteRule compiledRule,
        string sql,
        IReadOnlyList<RewriteParameterInfo> parameters)
    {
        var condition = compiledRule.Rule.When;
        var comparison = condition.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.IsNullOrEmpty(condition.SqlContains)
            && !sql.Contains(condition.SqlContains, comparison))
        {
            return ConditionMatchResult.NoMatch;
        }

        if (compiledRule.WhenSqlRegex is not null
            && !compiledRule.WhenSqlRegex.IsMatch(sql))
        {
            return ConditionMatchResult.NoMatch;
        }

        var hasParameterExists = !string.IsNullOrEmpty(condition.ParameterExists);
        var hasParameterNameRegex = compiledRule.WhenParameterNameRegex is not null;
        var hasParameterType = !string.IsNullOrEmpty(condition.ParameterType);
        var hasParameterFilter = hasParameterExists || hasParameterNameRegex || hasParameterType;

        if (!hasParameterFilter)
        {
            return ConditionMatchResult.Match([]);
        }

        var matchedParameters = parameters
            .Where(parameter => MatchesParameterNameFilter(compiledRule, condition, parameter))
            .Where(parameter => !hasParameterType || IsTypeMatch(parameter.TypeName, condition.ParameterType!))
            .ToArray();

        return matchedParameters.Length > 0
            ? ConditionMatchResult.Match(matchedParameters)
            : ConditionMatchResult.NoMatch;
    }

    private static string ApplySqlAction(string sql, CompiledSqlRewriteAction compiledAction)
    {
        var action = compiledAction.Action;

        return action.MatchType switch
        {
            SqlRewriteMatchType.Contains => ApplyContainsRule(sql, action),
            SqlRewriteMatchType.Regex when compiledAction.FindRegex is not null
                => compiledAction.FindRegex.Replace(sql, action.Replace),
            SqlRewriteMatchType.Regex => sql,
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

    private static bool MatchesParameterNameFilter(
        CompiledSqlRewriteRule compiledRule,
        SqlRewriteCondition condition,
        RewriteParameterInfo parameter)
    {
        var hasParameterExists = !string.IsNullOrEmpty(condition.ParameterExists);
        var hasParameterNameRegex = compiledRule.WhenParameterNameRegex is not null;

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
            return compiledRule.WhenParameterNameRegex!.IsMatch(parameter.Name);
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
