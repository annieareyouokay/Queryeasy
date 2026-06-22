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
        IReadOnlyCollection<string> parameterNames)
    {
        var rewrittenSql = sql;
        var matchedRules = new List<string>();
        var parameterChanges = new List<RewriteParameterChange>();

        foreach (var rule in _rules)
        {
            if (!IsScopeMatch(rule, scope) || !IsConditionMatch(rule, rewrittenSql, parameterNames))
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
                            if (!string.IsNullOrWhiteSpace(action.Name))
                            {
                                parameterChanges.Add(new RewriteParameterChange(
                                    action.Name,
                                    action.Value,
                                    null,
                                    rule.Name));
                                ruleChanged = true;
                            }

                            break;

                        case SqlRewriteActionType.SetParameterType:
                            if (!string.IsNullOrWhiteSpace(action.Name))
                            {
                                if (string.IsNullOrWhiteSpace(action.SqlType))
                                {
                                    return RewriteResult.Failed(
                                        sql,
                                        $"Rule '{rule.Name}' action SetParameterType for '{action.Name}' requires SqlType.");
                                }

                                parameterChanges.Add(new RewriteParameterChange(
                                    action.Name,
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

    private static bool IsConditionMatch(
        SqlRewriteRule rule,
        string sql,
        IReadOnlyCollection<string> parameterNames)
    {
        var condition = rule.When;
        var comparison = condition.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.IsNullOrEmpty(condition.SqlContains)
            && !sql.Contains(condition.SqlContains, comparison))
        {
            return false;
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
                return false;
            }
        }

        return string.IsNullOrEmpty(condition.ParameterExists)
            || parameterNames.Any(parameterName => IsParameterNameMatch(parameterName, condition.ParameterExists));
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

    private static bool IsParameterNameMatch(string actual, string expected)
    {
        return string.Equals(NormalizeParameterName(actual), NormalizeParameterName(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeParameterName(string name)
    {
        return name.StartsWith('@') ? name[1..] : name;
    }
}
