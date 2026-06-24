# Логирование и метрики

> **Для кого:** оператор  
> **Время чтения:** ~6 мин  
> **Что узнаете:** формат логов, уровни детализации и periodic summary.

## Формат строки лога

Каждая строка содержит:

- **UTC timestamp** (ISO 8601);
- **уровень:** `Error`, `Warn`, `Info`, `Debug`, `Trace`;
- **идентификатор сессии** в квадратных скобках, например `[a1b2c3d4]`;
- текст сообщения.

Реализация: [ProxyLog.cs](../Queryeasy.Proxy/ProxyLog.cs). При `AsyncLogging: true` (по умолчанию) вывод идёт через фоновый channel.

## Что попадает в лог

- Подключение клиента и подключение к SQL Server.
- PreLogin ENCRYPTION до и после обработки (при включённом `LogPreLoginOptions` и достаточном `LogLevel`).
- TDS-пакеты: тип, status, length, packetId.
- Hex-preview payload (при `LogPayloadPreview` и `LogLevel: Trace`).
- SQL Batch (декодированный текст).
- RPC `sp_executesql`: statement, объявление `@params`, значения параметров.
- Совпадения и применение rewrite-правил; warnings parse/encode.
- Закрытие сессии и объём переданных байтов.
- Periodic metrics summary.
- Per-request waterfall (`LogLevel: Trace`).

## Управление подробностью

| Параметр | Эффект |
| --- | --- |
| `LogLevel` | Минимальный уровень сообщений. SQL-текст — `Debug`/`Trace`; payload preview — `Trace`; waterfall — `Trace`. |
| `LogSqlText` | Включить decode и вывод SQL. Без этого SQL Batch может не декодироваться (см. [operating-modes.md](operating-modes.md)). |
| `LogRewriteSqlText` | SQL после rewrite (нужны `LogSqlText` и `Debug`/`Trace`). |
| `LogPayloadPreview` | Hex-preview байтов payload. |
| `PayloadPreviewBytes` | Длина preview; `0` — выключено. |
| `MaxSqlLogChars` | Усечение длинного SQL в логе; `0` — без усечения. |
| `LogPreLoginOptions` | Лог ENCRYPTION в PreLogin (`Debug`/`Trace`). |
| `EnablePerformanceMetrics` | Замеры wall-clock времени по этапам пайплайна (см. ниже). По умолчанию `false`. |

### Production defaults

В [appsettings.Production.json](../Queryeasy.Proxy/appsettings.Production.json):

```json
"LogLevel": "Warn",
"LogPayloadPreview": false,
"LogSqlText": false,
"LogRewriteSqlText": false,
"PayloadPreviewBytes": 0,
"MaxSqlLogChars": 0,
"LogPreLoginOptions": false
```

Подробное SQL/payload логирование в production включайте только временно и точечно. См. [security.md](security.md).

## Periodic metrics summary

При `MetricsSummaryIntervalSeconds` > 0 [SqlProxyServer](../Queryeasy.Proxy/SqlProxyServer.cs) периодически пишет summary из [ProxyMetrics.BuildSummary()](../Queryeasy.Proxy/ProxyMetrics.cs):

| Метрика | Значение |
| --- | --- |
| `active_sessions` | Текущие открытые сессии |
| `accepted_sessions` | Всего принято |
| `rejected_sessions` | Отклонено (overload) |
| `client_to_sql_bytes` | Байт client → sql |
| `sql_to_client_bytes` | Байт sql → client |
| `sql_batches` | Обработано SQL Batch (inspect) |
| `rpc_requests` | Обработано RPC (inspect) |
| `rewrite_matched` | Совпадений правил |
| `rewrite_applied` | Применённых rewrite (режим Rewrite) |
| `rewrite_failed` | Ошибок rewrite |
| `encode_failed` | Ошибок перекодирования TDS |
| `parse_warnings` | Предупреждений парсера |
| `raw_tls_fallbacks` | Переходов на raw TLS forwarding |
| `oversized_messages` | Сообщений больше `MaxInspectableMessageBytes` |

Интервал `0` отключает summary.

## Performance metrics (замеры времени)

Параметр **`EnablePerformanceMetrics`** включает wall-clock замеры по этапам TDS-пайплайна. По умолчанию **`false`** — включайте только на время profiling.

Реализация: [ProxyPerformanceMetrics.cs](../Queryeasy.Proxy/ProxyPerformanceMetrics.cs), [SessionPerformanceTracker.cs](../Queryeasy.Proxy/SessionPerformanceTracker.cs), [StagePercentileTracker.cs](../Queryeasy.Proxy/StagePercentileTracker.cs), [RequestTrace.cs](../Queryeasy.Proxy/RequestTrace.cs).

### Periodic perf summary

При `EnablePerformanceMetrics: true` и `MetricsSummaryIntervalSeconds` > 0 после строки `metrics ...` выводится многострочный блок `perf`:

```text
=== Performance Summary ===
  Session stages:
    SessionConnect              n=5        avg= 0.20ms  p50= 0.15ms  p95= 0.35ms  p99= 0.40ms  max= 0.50ms
    SessionPreLogin             n=5        avg= 1.50ms  p50= 1.20ms  p95= 2.10ms  p99= 2.50ms  max= 3.00ms
    SessionClientToServer       n=5        avg= 450ms   p50= 420ms   p95= 520ms   p99= 550ms   max= 580ms
    SessionServerToClient       n=5        avg= 380ms   p50= 350ms   p95= 450ms   p99= 480ms   max= 500ms

  Client -> SQL Server (C2S) stages:
    C2sReadMessage              n=15420    avg= 0.20ms  p50= 0.15ms  p95= 0.80ms  p99= 1.50ms  max=15.00ms
    C2sSqlBatchDecode           n=12000    avg= 0.30ms  p50= 0.20ms  p95= 1.20ms  p99= 2.00ms  max= 8.00ms
    C2sSqlBatchRewrite          n=8500     avg= 1.20ms  p50= 0.80ms  p95= 3.50ms  p99= 8.00ms  max=45.00ms
    C2sRpcInspect               n=3400     avg= 0.40ms  p50= 0.30ms  p95= 1.50ms  p99= 3.00ms  max=10.00ms
    C2sRpcRewrite               n=2100     avg= 0.90ms  p50= 0.60ms  p95= 2.80ms  p99= 5.00ms  max=30.00ms
    C2sWritePackets             n=54300    avg= 0.20ms  p50= 0.15ms  p95= 0.80ms  p99= 1.50ms  max=20.00ms

  SQL Server -> Client (S2C) stages:
    S2cRead                     n=98000    avg= 0.10ms  p50= 0.08ms  p95= 0.30ms  p99= 1.00ms  max=45.00ms
    S2cWrite                    n=98000    avg= 0.10ms  p50= 0.08ms  p95= 0.30ms  p99= 1.00ms  max=40.00ms
```

Формат каждого этапа: **`Stage`** с count, avg, p50, p95, p99, max.

Перцентили (p50/p95/p99) накапливаются на bounded-буфере (10 000 отсчётов) и сбрасываются каждый интервал summary.

При `LogLevel: Debug` дополнительно выводится JSON-версия статистики для машинной обработки:

```json
{"ts":"2026-06-24T10:00:00Z","stages":{"C2sReadMessage":{"count":15420,"avgMs":0.200,"p50Ms":0.150,"p95Ms":0.800,"p99Ms":1.500,"maxMs":15.000},"...}}
```

Основные этапы:

| Stage | Что измеряет |
| --- | --- |
| `SessionConnect` | TCP connect к SQL Server |
| `SessionPreLogin` | PreLogin negotiate |
| `SessionClientToServer` | Весь client → sql pipeline |
| `SessionServerToClient` | Весь sql → client copy |
| `C2sSqlBatchDecode` / `C2sRpcInspect` | Parse SQL Batch / RPC |
| `C2sSqlBatchRewrite` / `C2sRpcRewrite` | SqlRewriter |
| `C2sWritePackets` | Запись на SQL Server |
| `S2cRead` / `S2cWrite` | sql → client |

### Breakdown по сессии

При закрытии сессии и `LogLevel: Debug` (или `Trace`) — одна строка с суммой времени по этапам **этой сессии**:

```text
[Debug] [a1b2c3d4] perf session SessionPreLogin=2ms C2sRpcInspect=1ms
```

### Per-request waterfall (LogLevel: Trace)

При `LogLevel: Trace` каждый запрос выводится отдельным блоком с разбивкой времени по этапам и end-to-end временем (c2s + s2c):

```text
[Trace] [a1b2c3d4] --- Req #42 [SqlBatch] 25.3ms end-to-end (c2s=12.1ms, s2cWait=5.2ms, s2c=8.0ms) ---
  +0.0ms   c2s:readMessage            0.3ms
  +0.8ms   c2s:sqlBatchDecode         0.7ms
  +1.5ms   c2s:sqlBatchRewrite        5.2ms  ***
  +6.7ms   c2s:encodeSplit            1.1ms
  +7.8ms   c2s:writePackets           1.0ms
  +8.8ms   >>> sent to SQL Server
  +14.0ms  <<< response from SQL Server
  SQL: SELECT * FROM users WHERE id > 1000 ORDER BY name
```

- **c2s**: время обработки запроса прокси-сервером (read, decode, rewrite, encode, write)
- **s2cWait**: время ожидания ответа от SQL Server (round-trip до SQL Server)
- **s2c**: время передачи ответа от SQL Server клиенту
- **end-to-end**: полное время от получения запроса до отправки ответа клиенту
- *** на медленных этапах (>10ms) — помечаются звёздочкой для быстрой визуальной идентификации

### Как интерпретировать

- Высокий **s2cWait** указывает на медленный SQL Server (запрос выполняется долго).
- Сравнивайте режимы (`ForwardOnly`, `InspectOnly`, `Rewrite`) через [load-harness.ps1](../tools/load-harness.ps1) с `-ReuseConnection`.
- Перцентили p95/p99 показывают «хвост» распределения — реальную нагрузку на пиках.

Пример включения:

```json
"EnablePerformanceMetrics": true,
"MetricsSummaryIntervalSeconds": 30,
"LogLevel": "Debug"
```

Для per-request waterfall:

```json
"LogLevel": "Trace"
```

### Параметры конфигурации

| Параметр | По умолчанию | Описание |
| --- | --- | --- |
| `EnablePerformanceMetrics` | `false` | Включить замеры производительности |
| `PerformanceTraceBufferCapacity` | `1000` | Размер буфера трасс на сессию (0 = отключить per-request трассировку) |
| `PerformanceJsonLogPath` | `null` | Путь к файлу для записи JSON-статистики (например, `"C:\\logs\\perf.json"`). Каждые `MetricsSummaryIntervalSeconds` в файл дописывается строка JSON. `null` отключает запись в файл. |

## Минимальный overhead в production

- `LogLevel: "Warn"` или `"Info"`, `LogSqlText: false`, `LogPayloadPreview: false`
- Rewrite rules только для нужного `Scope` (например, только `RpcSpExecuteSql`)
- `AsyncLogging: true` (по умолчанию)
- `EnablePerformanceMetrics: false` (включать только для profiling)

## См. также

- [Конфигурация](configuration.md)
- [Troubleshooting](troubleshooting.md)
- [Безопасность](security.md)
