# Разработка

> **Для кого:** разработчик  
> **Время чтения:** ~10 мин  
> **Что узнаете:** как собирать, тестировать и расширять Queryeasy.

## Требования

- .NET SDK с поддержкой `net10.0`.
- Для ручной проверки — доступный SQL Server.
- Для load harness — PowerShell 7+.

## Команды

```powershell
dotnet build .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.Production.json
dotnet test .\Queryeasy.Proxy.Tests\Queryeasy.Proxy.Tests.csproj
dotnet publish .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -c Release
```

В репозитории нет `.sln` — все команды ссылаются на `.csproj`.

## Структура проектов

| Проект | Target | Назначение |
| --- | --- | --- |
| [Queryeasy.Proxy](../Queryeasy.Proxy/Queryeasy.Proxy.csproj) | `net10.0` | Console EXE — TDS proxy |
| [Queryeasy.Proxy.Tests](../Queryeasy.Proxy.Tests/Queryeasy.Proxy.Tests.csproj) | `net10.0` | xUnit unit tests |

Тесты получают доступ к `internal` типам через `InternalsVisibleTo` в [AssemblyInfo.cs](../Queryeasy.Proxy/Properties/AssemblyInfo.cs).

## Unit-тесты

Файл: [UnitTest1.cs](../Queryeasy.Proxy.Tests/UnitTest1.cs) — **18 тестов** (`[Fact]` / `[Theory]`).

| Область | Что проверяется |
| --- | --- |
| `TdsDateTime2Helper` | Round-trip encode/decode для scale 0, 3, 7 |
| `ParameterNameHelper` | Нормализация `@P1` → `P1` |
| `ProxyOptions` | Загрузка `RewriteRules` из корня JSON, hardening defaults |
| `SpExecuteSqlParameterHelper` | Parse `@params`, resolve по имени |
| `SqlRewriter` | Table/regex/type match, backward compat, invalid regex, SetParameterType validation |
| `InspectionCapabilities` | ForwardOnly, Rpc-only rules, InspectOnly |
| `TdsPacketWriter` | Один flush на multi-packet write |
| `RpcRequestInspector` | Lazy parse без full parse |
| `RpcSpExecuteSqlEncoder` | datetime2 scale rewrite в payload и declaration |

Автоматические тесты **не** покрывают end-to-end с реальным SQL Server — нужна ручная проверка по [getting-started.md](getting-started.md).

## Load harness

Скрипт: [tools/load-harness.ps1](../tools/load-harness.ps1) (PowerShell 7).

```powershell
pwsh .\tools\load-harness.ps1 `
  -ConnectionString "Server=127.0.0.1,11433;Database=testdb;Integrated Security=true;TrustServerCertificate=true" `
  -Query "SELECT 1" `
  -Concurrency 16 `
  -IterationsPerWorker 100
```

По умолчанию **`connection_mode=new_per_request`** — новое подключение на каждый запрос (нагружает login/handshake).

Для сценария ближе к пулу соединений (1С):

```powershell
pwsh .\tools\load-harness.ps1 `
  -ConnectionString "Server=127.0.0.1,11433;Database=testdb;User Id=sa;Password=...;TrustServerCertificate=true;Encrypt=false" `
  -Query "SELECT T1._Description FROM dbo._Reference47 T1" `
  -Concurrency 50 `
  -IterationsPerWorker 100 `
  -ReuseConnection
```

Сравните метрики для:

- прямого подключения к SQL Server;
- прокси в `ForwardOnly`;
- `InspectOnly`;
- `Rewrite`.

Фиксируйте `rps`, `latency_p50_ms`, `latency_p95_ms`. Harness **не** проверяет RPC rewrite — для `RpcSpExecuteSql` нужен клиент с `sp_executesql`.

## Точки расширения

### Новый тип rewrite action

1. Добавить значение в [SqlRewriteActionType.cs](../Queryeasy.Proxy/Rewrite/SqlRewriteActionType.cs).
2. Расширить [SqlRewriteAction.cs](../Queryeasy.Proxy/Rewrite/SqlRewriteAction.cs) (поля JSON).
3. Валидация в [ProxyOptions.ValidateRewriteAction](../Queryeasy.Proxy/ProxyOptions.cs).
4. Логика в [SqlRewriter.cs](../Queryeasy.Proxy/Rewrite/SqlRewriter.cs).
5. Если action меняет RPC payload — [RpcSpExecuteSqlEncoder.cs](../Queryeasy.Proxy/Tds/RpcSpExecuteSqlEncoder.cs).
6. Unit-тесты в [UnitTest1.cs](../Queryeasy.Proxy.Tests/UnitTest1.cs).

### Новый TDS message type для inspect

1. Обработчик в [TdsClientToServerPipeline.cs](../Queryeasy.Proxy/Tds/TdsClientToServerPipeline.cs) (ветка по `TdsPacketType`).
2. При необходимости — extractor/inspector в `Tds/`.
3. Обновить `InspectionCapabilities` в [ProxyOptions.GetInspectionCapabilities](../Queryeasy.Proxy/ProxyOptions.cs), если поведение зависит от mode/rules.
4. Метрики в [ProxyMetrics.cs](../Queryeasy.Proxy/ProxyMetrics.cs) при необходимости.

### Новое условие When

1. Поле в [SqlRewriteCondition.cs](../Queryeasy.Proxy/Rewrite/SqlRewriteCondition.cs).
2. Проверка в [CompiledSqlRewriteRule.cs](../Queryeasy.Proxy/Rewrite/CompiledSqlRewriteRule.cs) / [SqlRewriter.cs](../Queryeasy.Proxy/Rewrite/SqlRewriter.cs).
3. Валидация в `ProxyOptions` при загрузке.
4. Тесты.

### Новый RPC procedure (кроме sp_executesql)

Сейчас full parse и encode только для `sp_executesql`. Для другой процедуры потребуется:

- модель запроса (аналог [RpcSpExecuteSqlRequest.cs](../Queryeasy.Proxy/Tds/RpcSpExecuteSqlRequest.cs));
- inspector branch в [RpcRequestInspector.cs](../Queryeasy.Proxy/Tds/RpcRequestInspector.cs);
- encoder;
- расширение `QueryRewriteScope` при необходимости.

## Ручная проверка (checklist)

1. SQL Server на `TargetHost:TargetPort`.
2. Queryeasy в `InspectOnly`, `LogLevel: Debug`.
3. Клиент на `ListenHost:ListenPort`, `SELECT 1` — SQL Batch в логе.
4. `DryRun` + правило — match в логе, исходный SQL на сервере.
5. `Rewrite` — изменение видно в Profiler / логах.
6. `dotnet test` — все тесты green.

## Production hardening (для операторов)

См. [configuration.md](configuration.md) и [security.md](security.md). Кратко:

- `LogLevel: "Warn"`, без SQL/payload logs.
- `DryRun` перед `Rewrite`.
- Rules только для нужного `Scope`.
- `AsyncLogging: true`.

## См. также

- [Архитектура](architecture.md)
- [Правила rewrite](rewrite-rules.md)
- [Troubleshooting](troubleshooting.md)
