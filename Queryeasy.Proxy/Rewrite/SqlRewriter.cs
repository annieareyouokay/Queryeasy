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
        foreach (var rule in _rules)
        {
            if (string.IsNullOrEmpty(rule.Find))
            {
                continue;
            }

            try
            {
                var rewrittenSql = ApplyRule(sql, rule);

                if (!string.Equals(sql, rewrittenSql, StringComparison.Ordinal))
                {
                    return RewriteResult.ChangedBy(rewrittenSql, rule.Name);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                return RewriteResult.Failed(sql, $"Rule '{rule.Name}' failed: {ex.Message}");
            }
        }

        return RewriteResult.Unchanged(sql);
    }

    private static string ApplyRule(string sql, SqlRewriteRule rule)
    {
        return rule.MatchType switch
        {
            SqlRewriteMatchType.Contains => ApplyContainsRule(sql, rule),
            SqlRewriteMatchType.Regex => ApplyRegexRule(sql, rule),
            _ => sql
        };
    }

    private static string ApplyContainsRule(string sql, SqlRewriteRule rule)
    {
        var comparison = rule.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return sql.Replace(rule.Find, rule.Replace, comparison);
    }

    private static string ApplyRegexRule(string sql, SqlRewriteRule rule)
    {
        var options = RegexOptions.CultureInvariant;

        if (rule.IgnoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return Regex.Replace(sql, rule.Find, rule.Replace, options, TimeSpan.FromSeconds(1));
    }
}
