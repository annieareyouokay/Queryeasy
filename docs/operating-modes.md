# Режимы работы

> **Для кого:** оператор  
> **Время чтения:** ~8 мин  
> **Что узнаете:** четыре режима прокси, когда парсится SQL и как безопасно включать rewrite.

## Обзор режимов

Режим задаётся параметром `Mode` в секции `Proxy`. Значения определены в [ProxyMode.cs](../Queryeasy.Proxy/ProxyMode.cs).

### ForwardOnly

Минимальный overhead: прокси собирает multi-packet TDS message и пересылает без decode SQL, без RPC inspect и без rewrite. Подходит для passthrough и baseline benchmark.

### InspectOnly

Режим по умолчанию. Прокси декодирует SQL Batch и RPC (`sp_executesql` — statement и params при необходимости) и отправляет на SQL Server **исходные** пакеты без изменений.

### DryRun

Правила rewrite применяются к декодированному SQL Batch или RPC `sp_executesql` **только для проверки**. Совпадения логируются, на SQL Server уходят **исходные** TDS-пакеты.

Используйте перед `Rewrite`, чтобы убедиться, что правила срабатывают на нужных запросах.

### Rewrite

Включённые правила, которые реально изменили SQL Batch или `sp_executesql`, пересобирают TDS-пакеты и отправляют **изменённый** payload на SQL Server.

Для RPC rewrite поддерживается только **`sp_executesql`**: изменение SQL-текста, значений параметров и типов (в т.ч. смена scale у `datetime2` с обновлением бинарного TDS type info и строки `@params`).

## InspectionCapabilities

При старте прокси вычисляет [InspectionCapabilities](../Queryeasy.Proxy/ProxyOptions.cs) по `Mode`, `LogSqlText` и `Scope` включённых rewrite rules:

| Mode | SQL Batch decode | RPC inspect | Rewrite SQL Batch | Rewrite RPC |
| --- | --- | --- | --- | --- |
| `ForwardOnly` | нет | нет | нет | нет |
| `InspectOnly` | да | да | нет | нет |
| `DryRun` / `Rewrite` | если `LogSqlText` или есть rules для `SqlBatch`/`Any` | если `LogSqlText` или есть rules для `RpcSpExecuteSql`/`Any` | `DryRun`/`Rewrite` + rules | `DryRun`/`Rewrite` + rules |

### Почему при `LogSqlText: false` SQL Batch может не декодироваться

В production с правилами только для `RpcSpExecuteSql` и `LogSqlText: false`:

- SQL Batch **не** декодируется — меньше CPU на каждый batch.
- RPC `sp_executesql` **парсится** для rewrite.
- Остальные RPC — только заголовок процедуры (имя), без полного разбора.

Это поведение следует из `GetInspectionCapabilities()`: inspect SQL Batch включается при `LogSqlText`, rewrite SQL Batch или режиме `InspectOnly`.

## Поведение при ошибке rewrite

`RewriteFailureBehavior`:

| Значение | Поведение |
| --- | --- |
| `FailOpen` | Ошибка логируется, на SQL Server отправляется исходный SQL. |
| `FailClosed` | Сессия завершается ошибкой, исходный SQL не отправляется. |

## Рекомендованный порядок проверки rewrite

1. Запустите прокси в **`InspectOnly`** с `LogLevel: "Debug"` — убедитесь, что SQL Batch виден в логах ([logging-and-metrics.md](logging-and-metrics.md)).
2. Добавьте правило в `RewriteRules`, включите **`DryRun`** — проверьте совпадения в логах.
3. Включите **`LogRewriteSqlText`**, чтобы увидеть итоговый SQL после правила.
4. Переключите **`Mode`** на **`Rewrite`**.
5. Для критичных сценариев: `PreLoginEncryptionMode: "RequirePlainText"`, `FailIfEncryptionRequired: true`, `RewriteFailureBehavior: "FailClosed"` ([encryption-and-prelogin.md](encryption-and-prelogin.md)).

## Примеры конфигурации

### DryRun

```json
{
  "Proxy": {
    "ListenHost": "127.0.0.1",
    "ListenPort": 11433,
    "TargetHost": "127.0.0.1",
    "TargetPort": 1433,
    "LogLevel": "Debug",
    "Mode": "DryRun",
    "LogSqlText": true,
    "LogRewriteSqlText": true,
    "PreLoginEncryptionMode": "TryDisable",
    "RewriteFailureBehavior": "FailOpen"
  },
  "RewriteRules": [
    {
      "Name": "UseDebugView",
      "Enabled": true,
      "MatchType": "Contains",
      "Find": "FROM dbo.Users",
      "Replace": "FROM dbo.Users_Debug",
      "IgnoreCase": true
    }
  ]
}
```

### RPC Rewrite

```json
{
  "Proxy": {
    "Mode": "Rewrite",
    "LogLevel": "Debug",
    "LogSqlText": true,
    "LogRewriteSqlText": true,
    "PreLoginEncryptionMode": "TryDisable"
  },
  "RewriteRules": [
    {
      "Name": "ChangeP1DateTimeScale",
      "Enabled": true,
      "Scope": "RpcSpExecuteSql",
      "When": {
        "SqlContains": "T1._Fld58 = @P1",
        "ParameterExists": "@P1"
      },
      "Actions": [
        {
          "Type": "SetParameterType",
          "Name": "@P1",
          "SqlType": "datetime2(0)"
        }
      ]
    }
  ]
}
```

Подробнее о правилах: [rewrite-rules.md](rewrite-rules.md).

## См. также

- [Конфигурация](configuration.md)
- [Правила rewrite](rewrite-rules.md)
- [Troubleshooting](troubleshooting.md)
