using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Queryeasy.Proxy.Rewrite;
using Queryeasy.Proxy.Tds.PreLogin;

namespace Queryeasy.Proxy;

internal sealed class ProxyOptions
{
    public string ListenHost { get; init; } = IPAddress.Loopback.ToString();

    public int ListenPort { get; init; } = 11433;

    public string TargetHost { get; init; } = IPAddress.Loopback.ToString();

    public int TargetPort { get; init; } = 1433;

    public int ConnectTimeoutSeconds { get; init; } = 10;

    public int IdleTimeoutMinutes { get; init; } = 30;

    public int BufferSizeBytes { get; init; } = 81920;

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

    public List<SqlRewriteRule> RewriteRules { get; init; } = [];

    public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(ConnectTimeoutSeconds);

    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);

    public static ProxyOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Configuration file '{path}' was not found. Using defaults.");
            return new ProxyOptions();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("Proxy", out var proxySection))
        {
            Console.WriteLine("Configuration section 'Proxy' was not found. Using defaults.");
            return new ProxyOptions();
        }

        var serializerOptions = CreateSerializerOptions();
        var options = proxySection.Deserialize<ProxyOptions>(serializerOptions) ?? new ProxyOptions();

        if (document.RootElement.TryGetProperty("RewriteRules", out var rewriteRulesSection))
        {
            var rewriteRules = rewriteRulesSection.Deserialize<List<SqlRewriteRule>>(serializerOptions) ?? [];

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
        return new ProxyOptions
        {
            ListenHost = ListenHost,
            ListenPort = ListenPort,
            TargetHost = TargetHost,
            TargetPort = TargetPort,
            ConnectTimeoutSeconds = ConnectTimeoutSeconds,
            IdleTimeoutMinutes = IdleTimeoutMinutes,
            BufferSizeBytes = BufferSizeBytes,
            Mode = Mode,
            LogPayloadPreview = LogPayloadPreview,
            LogSqlText = LogSqlText,
            LogRewriteSqlText = LogRewriteSqlText,
            PayloadPreviewBytes = PayloadPreviewBytes,
            MaxSqlLogChars = MaxSqlLogChars,
            RewriteFailureBehavior = RewriteFailureBehavior,
            PreLoginEncryptionMode = PreLoginEncryptionMode,
            FailIfEncryptionRequired = FailIfEncryptionRequired,
            LogPreLoginOptions = LogPreLoginOptions,
            RewriteRules = rewriteRules
        };
    }
}
