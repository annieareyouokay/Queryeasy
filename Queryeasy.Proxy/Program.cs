using Queryeasy.Proxy;

var configPath = args.Length > 0
    ? args[0]
    : File.Exists("appsettings.json")
        ? "appsettings.json"
        : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
var options = ProxyOptions.Load(configPath);
options.Validate();

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

var server = new SqlProxyServer(options);

try
{
    await server.RunAsync(shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    Console.WriteLine("Proxy stopped.");
}
