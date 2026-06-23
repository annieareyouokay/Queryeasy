using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Queryeasy.Proxy.Rewrite;
using Queryeasy.Proxy.Tds.PreLogin;

namespace Queryeasy.Proxy;

internal sealed record ProxyOptions
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public string ListenHost { get; init; } = IPAddress.Loopback.ToString();

    public int ListenPort { get; init; } = 11433;

    public string TargetHost { get; init; } = IPAddress.Loopback.ToString();

    public int TargetPort { get; init; } = 1433;

    public int ConnectTimeoutSeconds { get; init; } = 10;

    public int IdleTimeoutMinutes { get; init; } = 30;

    public int BufferSizeBytes { get; init; } = 81920;

    public ProxyLogLevel LogLevel { get; init; } = ProxyLogLevel.Info;

    public ProxyMode Mode { get; init; } = ProxyMode.InspectOnly;

    public bool LogPayloadPreview { get; init; } = true;

    public bool LogSqlText { get; init; } = true;

    public bool LogRewriteSqlText { get; init; }

    public int PayloadPreviewBytes { get; init; } = 64;

    public int MaxSqlLogChars { get; init; } = 4000;

    public RewriteFailureBehavior RewriteFailureBehavior { get; init; } = RewriteFailureBehavior.FailOpen;

    public PreLoginEncryptionMode PreLoginEncryptionMode { get; init; } = PreLoginEncryptionMode.TryDisable;

    public bool FailIfEncryptionRequired { get; init; }

    public bool LogPreLoginOptions { get; init; } = true;

    public int MaxConcurrentSessions { get; init; } = 500;

    public int MaxInspectableMessageBytes { get; init; } = 1_048_576;

    public int MaxRewriteSqlChars { get; init; } = 65_536;

    public bool RejectWhenOverloaded { get; init; } = true;

    public int MetricsSummaryIntervalSeconds { get; init; } = 30;

    public bool AsyncLogging { get; init; } = true;

    public List<SqlRewriteRule> RewriteRules { get; init; } = [];

    public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(ConnectTimeoutSeconds);

    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);

    public TimeSpan MetricsSummaryInterval => TimeSpan.FromSeconds(MetricsSummaryIntervalSeconds);

    public static ProxyOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            ProxyLog.Warn($"Configuration file '{path}' was not found. Using defaults.");
            return new ProxyOptions();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("Proxy", out var proxySection))
        {
            ProxyLog.Warn("Configuration section 'Proxy' was not found. Using defaults.");
            return new ProxyOptions();
        }

        var options = proxySection.Deserialize<ProxyOptions>(SerializerOptions) ?? new ProxyOptions();

        if (document.RootElement.TryGetProperty("RewriteRules", out var rewriteRulesSection))
        {
            var rewriteRules = rewriteRulesSection.Deserialize<List<SqlRewriteRule>>(SerializerOptions) ?? [];

            options = options.WithRewriteRules(rewriteRules);
        }

        return options;
    }

    public void Validate()
    {
        ValidatePort(ListenPort, nameof(ListenPort));
        ValidatePort(TargetPort, nameof(TargetPort));

        if (string.IsNullOrWhiteSpace(ListenHost))
        {
            throw new InvalidOperationException($"{nameof(ListenHost)} must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(TargetHost))
        {
            throw new InvalidOperationException($"{nameof(TargetHost)} must not be empty.");
        }

        if (ConnectTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException($"{nameof(ConnectTimeoutSeconds)} must be greater than zero.");
        }

        if (IdleTimeoutMinutes <= 0)
        {
            throw new InvalidOperationException($"{nameof(IdleTimeoutMinutes)} must be greater than zero.");
        }

        if (BufferSizeBytes < 4096)
        {
            throw new InvalidOperationException($"{nameof(BufferSizeBytes)} must be at least 4096.");
        }

        if (PayloadPreviewBytes < 0)
        {
            throw new InvalidOperationException($"{nameof(PayloadPreviewBytes)} must not be negative.");
        }

        if (MaxSqlLogChars < 0)
        {
            throw new InvalidOperationException($"{nameof(MaxSqlLogChars)} must not be negative.");
        }

        if (MaxConcurrentSessions <= 0)
        {
            throw new InvalidOperationException($"{nameof(MaxConcurrentSessions)} must be greater than zero.");
        }

        if (MaxInspectableMessageBytes < Tds.TdsPacket.HeaderLength)
        {
            throw new InvalidOperationException($"{nameof(MaxInspectableMessageBytes)} must be at least {Tds.TdsPacket.HeaderLength}.");
        }

        if (MaxRewriteSqlChars < 0)
        {
            throw new InvalidOperationException($"{nameof(MaxRewriteSqlChars)} must not be negative.");
        }

        if (MetricsSummaryIntervalSeconds < 0)
        {
            throw new InvalidOperationException($"{nameof(MetricsSummaryIntervalSeconds)} must not be negative.");
        }

        ValidateRewriteRules();
    }

    public InspectionCapabilities GetInspectionCapabilities()
    {
        if (Mode == ProxyMode.ForwardOnly)
        {
            return new InspectionCapabilities(
                IsForwardOnly: true,
                InspectSqlBatch: false,
                InspectRpc: false,
                RewriteSqlBatch: false,
                RewriteRpc: false,
                LogSqlText: LogSqlText);
        }

        var rewriteSqlBatch = Mode is ProxyMode.DryRun or ProxyMode.Rewrite
            && RewriteRules.Any(rule => rule.Enabled && AppliesToSqlBatch(rule.Scope));
        var rewriteRpc = Mode is ProxyMode.DryRun or ProxyMode.Rewrite
            && RewriteRules.Any(rule => rule.Enabled && AppliesToRpc(rule.Scope));

        var inspectSqlBatch = LogSqlText || rewriteSqlBatch || Mode == ProxyMode.InspectOnly;
        var inspectRpc = LogSqlText || rewriteRpc || Mode == ProxyMode.InspectOnly;

        return new InspectionCapabilities(
            IsForwardOnly: false,
            InspectSqlBatch: inspectSqlBatch,
            InspectRpc: inspectRpc,
            RewriteSqlBatch: rewriteSqlBatch,
            RewriteRpc: rewriteRpc,
            LogSqlText: LogSqlText);
    }

    private static bool AppliesToSqlBatch(QueryRewriteScope scope)
    {
        return scope is QueryRewriteScope.Any or QueryRewriteScope.SqlBatch;
    }

    private static bool AppliesToRpc(QueryRewriteScope scope)
    {
        return scope is QueryRewriteScope.Any or QueryRewriteScope.RpcSpExecuteSql;
    }

    private void ValidateRewriteRules()
    {
        foreach (var rule in RewriteRules.Where(rule => rule.Enabled))
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                throw new InvalidOperationException("Rewrite rule name must not be empty.");
            }

            ValidateRewriteCondition(rule);

            if (rule.Actions.Count == 0 && string.IsNullOrEmpty(rule.Find))
            {
                continue;
            }

            foreach (var action in rule.Actions)
            {
                ValidateRewriteAction(rule, action);
            }
        }

        _ = new SqlRewriter(RewriteRules);
    }

    private static void ValidateRewriteCondition(SqlRewriteRule rule)
    {
        var condition = rule.When;
        var hasParameterExists = !string.IsNullOrWhiteSpace(condition.ParameterExists);
        var hasParameterNameRegex = !string.IsNullOrWhiteSpace(condition.ParameterNameRegex);
        var hasParameterType = !string.IsNullOrWhiteSpace(condition.ParameterType);

        if (hasParameterType && !hasParameterExists && !hasParameterNameRegex)
        {
            throw new InvalidOperationException(
                $"Rule '{rule.Name}' When.ParameterType requires ParameterExists or ParameterNameRegex.");
        }
    }

    private static void ValidateRewriteAction(SqlRewriteRule rule, SqlRewriteAction action)
    {
        switch (action.Type)
        {
            case SqlRewriteActionType.ReplaceSql:
                if (string.IsNullOrEmpty(action.Find))
                {
                    throw new InvalidOperationException($"Rule '{rule.Name}' action ReplaceSql requires Find.");
                }

                break;

            case SqlRewriteActionType.SetParameterValue:
                ValidateParameterActionTarget(rule, action);

                if (action.Value is null)
                {
                    throw new InvalidOperationException(
                        $"Rule '{rule.Name}' action SetParameterValue for '{GetParameterActionLabel(action)}' requires Value.");
                }

                break;

            case SqlRewriteActionType.SetParameterType:
                ValidateParameterActionTarget(rule, action);

                if (string.IsNullOrWhiteSpace(action.SqlType))
                {
                    throw new InvalidOperationException(
                        $"Rule '{rule.Name}' action SetParameterType for '{GetParameterActionLabel(action)}' requires SqlType.");
                }

                break;
        }
    }

    private static void ValidateParameterActionTarget(SqlRewriteRule rule, SqlRewriteAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.Name))
        {
            return;
        }

        var condition = rule.When;
        if (!string.IsNullOrWhiteSpace(condition.ParameterExists)
            || !string.IsNullOrWhiteSpace(condition.ParameterNameRegex)
            || !string.IsNullOrWhiteSpace(condition.ParameterType))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Rule '{rule.Name}' action {action.Type} requires Name or a parameter filter in When.");
    }

    private static string GetParameterActionLabel(SqlRewriteAction action)
    {
        return string.IsNullOrWhiteSpace(action.Name) ? "matched parameters" : action.Name;
    }

    private static void ValidatePort(int port, string name)
    {
        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new InvalidOperationException($"{name} must be between {IPEndPoint.MinPort} and {IPEndPoint.MaxPort}.");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    private ProxyOptions WithRewriteRules(List<SqlRewriteRule> rewriteRules)
    {
        return this with { RewriteRules = rewriteRules };
    }
}
