#requires -Version 7.0

param(
    [Parameter(Mandatory = $true)]
    [string] $ConnectionString,

    [string] $Query = "SELECT 1",

    [int] $Concurrency = 16,

    [int] $IterationsPerWorker = 100,

    [switch] $ReuseConnection
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Data

$total = $Concurrency * $IterationsPerWorker
$latencies = [System.Collections.Concurrent.ConcurrentBag[double]]::new()
$errors = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$started = [System.Diagnostics.Stopwatch]::StartNew()

1..$Concurrency | ForEach-Object -Parallel {
    $connectionString = $using:ConnectionString
    $query = $using:Query
    $iterations = $using:IterationsPerWorker
    $reuseConnection = $using:ReuseConnection
    $latencies = $using:latencies
    $errors = $using:errors

    if ($reuseConnection) {
        $connection = [System.Data.SqlClient.SqlConnection]::new($connectionString)
        $command = $connection.CreateCommand()
        $command.CommandText = $query

        try {
            $connection.Open()

            for ($i = 0; $i -lt $iterations; $i++) {
                $timer = [System.Diagnostics.Stopwatch]::StartNew()

                try {
                    [void] $command.ExecuteScalar()
                    $timer.Stop()
                    $latencies.Add($timer.Elapsed.TotalMilliseconds)
                }
                catch {
                    $timer.Stop()
                    $errors.Add($_.Exception.Message)
                }
            }
        }
        catch {
            $errors.Add($_.Exception.Message)
        }
        finally {
            $connection.Dispose()
        }

        return
    }

    for ($i = 0; $i -lt $iterations; $i++) {
        $timer = [System.Diagnostics.Stopwatch]::StartNew()

        try {
            $connection = [System.Data.SqlClient.SqlConnection]::new($connectionString)
            $command = $connection.CreateCommand()
            $command.CommandText = $query
            $connection.Open()
            [void] $command.ExecuteScalar()
            $connection.Dispose()
            $timer.Stop()
            $latencies.Add($timer.Elapsed.TotalMilliseconds)
        }
        catch {
            $timer.Stop()
            $errors.Add($_.Exception.Message)
        }
    }
} -ThrottleLimit $Concurrency

$started.Stop()
$ordered = @($latencies.ToArray() | Sort-Object)

function Get-Percentile([double[]] $Values, [double] $Percentile) {
    if ($Values.Count -eq 0) {
        return 0
    }

    $index = [Math]::Min($Values.Count - 1, [Math]::Floor(($Values.Count - 1) * $Percentile))
    return [Math]::Round($Values[$index], 2)
}

$connectionMode = if ($ReuseConnection) { "reuse" } else { "new_per_request" }

Write-Host "connection_mode=$connectionMode"
Write-Host "requests_total=$total"
Write-Host "requests_ok=$($ordered.Count)"
Write-Host "requests_failed=$($errors.Count)"
Write-Host "elapsed_seconds=$([Math]::Round($started.Elapsed.TotalSeconds, 2))"
Write-Host "rps=$([Math]::Round($ordered.Count / [Math]::Max(0.001, $started.Elapsed.TotalSeconds), 2))"
Write-Host "latency_p50_ms=$(Get-Percentile $ordered 0.50)"
Write-Host "latency_p95_ms=$(Get-Percentile $ordered 0.95)"
Write-Host "latency_p99_ms=$(Get-Percentile $ordered 0.99)"

if ($errors.Count -gt 0) {
    Write-Host "first_error=$($errors.ToArray()[0])"
    exit 1
}
