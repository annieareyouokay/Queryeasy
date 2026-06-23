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

## Управление подробностью

| Параметр | Эффект |
| --- | --- |
| `LogLevel` | Минимальный уровень сообщений. SQL-текст — `Debug`/`Trace`; payload preview — `Trace`. |
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

Реализация: [ProxyPerformanceMetrics.cs](../Queryeasy.Proxy/ProxyPerformanceMetrics.cs), [SessionPerformanceTracker.cs](../Queryeasy.Proxy/SessionPerformanceTracker.cs).

### Periodic perf summary

При `EnablePerformanceMetrics: true` и `MetricsSummaryIntervalSeconds` > 0 после строки `metrics ...` выводится строка `perf`:

```text
[Info] perf C2sReadPacket=120/6000ms/avg50/max200 SessionConnect=5/250ms/avg50/max100
```

Формат каждого этапа: **`Stage=count/total_ms/avgXX/maxYY`**.

Основные этапы:

| Stage | Что измеряет |
| --- | --- |
| `SessionConnect` | TCP connect к SQL Server |
| `SessionPreLogin` | PreLogin negotiate |
| `SessionClientToServer` | Весь client → sql pipeline |
| `SessionServerToClient` | Весь sql → client copy |
| `C2sReadPacket` | Чтение TDS-пакета (включая ожидание сети) |
| `C2sSqlBatchDecode` / `C2sRpcInspect` | Parse SQL Batch / RPC |
| `C2sSqlBatchRewrite` / `C2sRpcRewrite` | SqlRewriter |
| `C2sWritePackets` | Запись на SQL Server |
| `S2cRead` / `S2cWrite` | sql → client |

### Breakdown по сессии

При закрытии сессии и `LogLevel: Debug` (или `Trace`) — одна строка с суммой времени по этапам **этой сессии**:

```text
[Debug] [a1b2c3d4] perf session SessionPreLogin=2ms C2sReadPacket=380ms C2sRpcInspect=1ms
```

### Как интерпретировать

- Замеры **wall-clock**: `C2sReadPacket` включает ожидание данных от клиента, не только CPU прокси.
- Высокий `C2sReadPacket` при низком `C2sRpcRewrite` — узкое место в сети/клиенте, не в rewrite.
- Сравнивайте режимы (`ForwardOnly`, `InspectOnly`, `Rewrite`) через [load-harness.ps1](../tools/load-harness.ps1) с `-ReuseConnection`.

Пример включения:

```json
"EnablePerformanceMetrics": true,
"MetricsSummaryIntervalSeconds": 30,
"LogLevel": "Debug"
```

## Минимальный overhead в production

- `LogLevel: "Warn"` или `"Info"`, `LogSqlText: false`, `LogPayloadPreview: false`
- Rewrite rules только для нужного `Scope` (например, только `RpcSpExecuteSql`)
- `AsyncLogging: true` (по умолчанию)
- `EnablePerformanceMetrics: false` (включать только для profiling)

## См. также

- [Конфигурация](configuration.md)
- [Troubleshooting](troubleshooting.md)
- [Безопасность](security.md)
