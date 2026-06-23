using System.Text.RegularExpressions;

namespace Queryeasy.Proxy.Rewrite;

internal sealed class CompiledSqlRewriteRule
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private CompiledSqlRewriteRule(
        SqlRewriteRule rule,
        Regex? whenSqlRegex,
        Regex? whenParameterNameRegex,
        IReadOnlyList<CompiledSqlRewriteAction> actions)
    {
        Rule = rule;
        WhenSqlRegex = whenSqlRegex;
        WhenParameterNameRegex = whenParameterNameRegex;
        Actions = actions;
    }

    public SqlRewriteRule Rule { get; }

    public Regex? WhenSqlRegex { get; }

    public Regex? WhenParameterNameRegex { get; }

    public IReadOnlyList<CompiledSqlRewriteAction> Actions { get; }

    public static CompiledSqlRewriteRule Create(SqlRewriteRule rule)
    {
        var condition = rule.When;
        var whenSqlRegex = CompileOptional(
            condition.SqlRegex,
            condition.IgnoreCase,
            $"Rule '{rule.Name}' When.SqlRegex");
        var whenParameterNameRegex = CompileOptional(
            condition.ParameterNameRegex,
            condition.IgnoreCase,
            $"Rule '{rule.Name}' When.ParameterNameRegex");

        var actions = BuildActions(rule);

        return new CompiledSqlRewriteRule(rule, whenSqlRegex, whenParameterNameRegex, actions);
    }

    private static IReadOnlyList<CompiledSqlRewriteAction> BuildActions(SqlRewriteRule rule)
    {
        if (rule.Actions.Count > 0)
        {
            return rule.Actions
                .Select(action => CompiledSqlRewriteAction.Create(action, rule.Name))
                .ToArray();
        }

        if (string.IsNullOrEmpty(rule.Find))
        {
            return [];
        }

        return
        [
            CompiledSqlRewriteAction.Create(
                new SqlRewriteAction
                {
                    Type = SqlRewriteActionType.ReplaceSql,
                    MatchType = rule.MatchType,
                    Find = rule.Find,
                    Replace = rule.Replace,
                    IgnoreCase = rule.IgnoreCase
                },
                rule.Name)
        ];
    }

    internal static Regex? CompileOptional(string? pattern, bool ignoreCase, string label)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

            if (ignoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            return new Regex(pattern, options, RegexTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexParseException)
        {
            throw new InvalidOperationException($"{label} is invalid: {ex.Message}", ex);
        }
    }
}

internal sealed class CompiledSqlRewriteAction
{
    private CompiledSqlRewriteAction(SqlRewriteAction action, Regex? findRegex)
    {
        Action = action;
        FindRegex = findRegex;
    }

    public SqlRewriteAction Action { get; }

    public Regex? FindRegex { get; }

    public static CompiledSqlRewriteAction Create(SqlRewriteAction action, string ruleName)
    {
        Regex? findRegex = null;

        if (action.Type == SqlRewriteActionType.ReplaceSql
            && action.MatchType == SqlRewriteMatchType.Regex
            && !string.IsNullOrEmpty(action.Find))
        {
            findRegex = CompiledSqlRewriteRule.CompileOptional(
                action.Find,
                action.IgnoreCase,
                $"Rule '{ruleName}' action ReplaceSql Find regex");
        }

        return new CompiledSqlRewriteAction(action, findRegex);
    }
}
