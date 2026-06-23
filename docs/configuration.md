# Конфигурация

> **Для кого:** оператор  
> **Время чтения:** ~10 мин  
> **Что узнаете:** структуру JSON-конфига, справочник параметров `Proxy` и правила валидации.

## Структура файла

Основной файл: [Queryeasy.Proxy/appsettings.json](../Queryeasy.Proxy/appsettings.json).

```json
{
  "Proxy": {
    "ListenHost": "127.0.0.1",
    "ListenPort": 11433,
    "TargetHost": "127.0.0.1",
    "TargetPort": 1433,
    "ConnectTimeoutSeconds": 10,
    "IdleTimeoutMinutes": 30,
    "BufferSizeBytes": 81920,
    "LogLevel": "Info",
    "Mode": "InspectOnly",
    "LogPayloadPreview": true,
    "LogSqlText": true,
    "LogRewriteSqlText": false,
    "PayloadPreviewBytes": 64,
    "MaxSqlLogChars": 4000,
    "RewriteFailureBehavior": "FailOpen",
    "PreLoginEncryptionMode": "TryDisable",
    "FailIfEncryptionRequired": false,
    "LogPreLoginOptions": true,
    "MaxConcurrentSessions": 500,
    "MaxInspectableMessageBytes": 1048576,
    "MaxRewriteSqlChars": 65536,
    "RejectWhenOverloaded": true,
    "MetricsSummaryIntervalSeconds": 30,
    "AsyncLogging": true
  },
  "RewriteRules": []
}
```

- Секция **`Proxy`** — настройки прокси.
- Массив **`RewriteRules`** — в **корне** JSON, не внутри `Proxy` (загружается отдельно в [ProxyOptions.cs](../Queryeasy.Proxy/ProxyOptions.cs)).

JSON-поля десериализуются без учёта регистра. Enum-значения задаются строками.

## Загрузка конфигурации

Логика в [Program.cs](../Queryeasy.Proxy/Program.cs):

1. Первый аргумент CLI → путь к файлу.
2. Иначе `appsettings.json` в CWD.
3. Иначе `appsettings.json` рядом с exe.
4. Иначе defaults.

При старте вызывается `ProxyOptions.Validate()` — некорректный конфиг приводит к ошибке до accept loop.

## Копирование в output

В [Queryeasy.Proxy.csproj](../Queryeasy.Proxy/Queryeasy.Proxy.csproj) настроено `CopyToOutputDirectory` для:

- `appsettings.json`
- `appsettings.Production.json`

Остальные `appsettings.*.json` **не** копируются автоматически.

## Справочник параметров Proxy

| Параметр | По умолчанию | Описание |
| --- | --- | --- |
| `ListenHost` | `127.0.0.1` | Хост или IP, на котором прокси принимает подключения. |
| `ListenPort` | `11433` | TCP-порт прокси. |
| `TargetHost` | `127.0.0.1` | Хост или IP реального SQL Server. |
| `TargetPort` | `1433` | TCP-порт SQL Server. |
| `ConnectTimeoutSeconds` | `10` | Таймаут подключения к целевому SQL Server. |
| `IdleTimeoutMinutes` | `30` | Таймаут простоя чтения в сессии. |
| `BufferSizeBytes` | `81920` | Размер буфера для raw byte forwarding. Минимум `4096`. |
| `LogLevel` | `Info` | Минимальный уровень: `Error`, `Warn`, `Info`, `Debug`, `Trace`. |
| `Mode` | `InspectOnly` | Режим работы — см. [operating-modes.md](operating-modes.md). |
| `LogPayloadPreview` | `true` | Hex-preview payload TDS-пакетов. Требует `LogLevel: "Trace"`. |
| `LogSqlText` | `true` | Логировать декодированный SQL. Требует `LogLevel: "Debug"` или `Trace`. |
| `LogRewriteSqlText` | `false` | Логировать SQL после rewrite. Требует `LogSqlText` и `LogLevel: "Debug"` или `Trace`. |
| `PayloadPreviewBytes` | `64` | Сколько байт payload в preview. `0` отключает preview. |
| `MaxSqlLogChars` | `4000` | Макс. длина SQL в логе. `0` — без усечения. |
| `RewriteFailureBehavior` | `FailOpen` | Поведение при ошибке rewrite: `FailOpen` / `FailClosed`. |
| `PreLoginEncryptionMode` | `TryDisable` | Обработка PreLogin ENCRYPTION — см. [encryption-and-prelogin.md](encryption-and-prelogin.md). |
| `FailIfEncryptionRequired` | `false` | Завершать сессию при raw TLS после PreLogin. |
| `LogPreLoginOptions` | `true` | Логировать ENCRYPTION в PreLogin. Требует `LogLevel: "Debug"` или `Trace`. |
| `MaxConcurrentSessions` | `500` | Макс. одновременных сессий. |
| `MaxInspectableMessageBytes` | `1048576` | Макс. payload TDS message для inspect/rewrite. При превышении — forward без inspect. |
| `MaxRewriteSqlChars` | `65536` | Макс. длина SQL для rewrite engine. `0` — без лимита. |
| `RejectWhenOverloaded` | `true` | При исчерпании лимита сессий — сразу закрыть новое подключение. Если `false` — ждать слот. |
| `MetricsSummaryIntervalSeconds` | `30` | Интервал summary-лога метрик. `0` отключает. |
| `AsyncLogging` | `true` | Логи через фоновый channel вместо синхронного вывода. |

## Валидация при запуске

Проверяется ([ProxyOptions.Validate](../Queryeasy.Proxy/ProxyOptions.cs)):

- Порты в диапазоне `0..65535`, хосты не пустые.
- Таймауты больше нуля.
- `BufferSizeBytes` ≥ `4096`.
- `MaxConcurrentSessions` > 0.
- `MaxInspectableMessageBytes` ≥ размер TDS header.
- Числовые лимиты логов/метрик не отрицательные.
- Включённые rewrite rules: имена, условия, обязательные поля actions; компиляция regex (таймаут 1 с).

## Production-конфиг

Пример: [appsettings.Production.json](../Queryeasy.Proxy/appsettings.Production.json).

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.Production.json
```

Ключевые значения production:

- `LogLevel: "Warn"`, `LogSqlText: false`, `LogPayloadPreview: false`
- `Mode: "Rewrite"` с правилами только для нужного `Scope`
- `RewriteFailureBehavior: "FailOpen"` до стабилизации правил

Перед переключением на `Rewrite` прогоните правила в `DryRun`. Подробнее: [logging-and-metrics.md](logging-and-metrics.md), [security.md](security.md).

## См. также

- [Правила rewrite](rewrite-rules.md)
- [Режимы работы](operating-modes.md)
- [Быстрый старт](getting-started.md)
