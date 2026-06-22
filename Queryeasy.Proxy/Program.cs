using Queryeasy.Proxy;

var configPath = ResolveConfigPath(args);
var options = ProxyOptions.Load(configPath);
options.Validate();
ProxyLog.Configure(options.LogLevel);
var metrics = new ProxyMetrics();

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

var server = new SqlProxyServer(options, metrics);

try
{
    await server.RunAsync(shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    ProxyLog.Info("Proxy stopped.");
}

static string ResolveConfigPath(string[] args)
{
    if (args.Length > 0)
    {
        return args[0];
    }

    return File.Exists("appsettings.json")
        ? "appsettings.json"
        : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
}
