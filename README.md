# Queryeasy

Queryeasy - это локальный TCP-прокси для Microsoft SQL Server, работающий на уровне TDS (Tabular Data Stream). Проект принимает подключения SQL-клиентов на локальном адресе, перенаправляет трафик на реальный SQL Server и позволяет инспектировать клиентские TDS-пакеты, логировать SQL-запросы и при необходимости переписывать SQL Batch или RPC `sp_executesql` перед отправкой на сервер.

Проект полезен для разработки, отладки и экспериментов с SQL-трафиком, когда нужно увидеть, какие запросы отправляет приложение, проверить поведение клиента при измененном SQL или безопасно протестировать правила переписывания в режиме dry run.

## Возможности

- Принимает TCP-подключения от SQL-клиентов и проксирует их на целевой SQL Server.
- Логирует TDS-пакеты в направлении `client -> sql` с управляемым `LogLevel`.
- Декодирует и логирует SQL Batch.
- Инспектирует RPC Request и декодирует `sp_executesql`: SQL-текст, объявление параметров и значения параметров.
- Поддерживает правила переписывания SQL Batch и RPC `sp_executesql`.
- Для `sp_executesql` умеет менять SQL-текст, значения параметров и тип параметра, включая смену scale у `datetime2`.
- Позволяет запускать rewrite в режиме `DryRun`, чтобы увидеть совпадения без изменения трафика.
- Добавляет лимиты на concurrent sessions и размер сообщений для inspect/rewrite.
- Пишет периодический summary log с in-process метриками.
- Умеет пытаться отключить TDS-шифрование на этапе PreLogin, если это разрешено клиентом и сервером.
- При обнаружении raw TLS может перейти в обычное байтовое проксирование или завершить сессию в fail-closed режиме.

## Статус проекта

Текущая версия состоит из консольного proxy-проекта, test project и вспомогательных scripts:

```text
Queryeasy/
├── Queryeasy.Proxy/
│   ├── Program.cs
│   ├── ProxyOptions.cs
│   ├── ProxySession.cs
│   ├── SqlProxyServer.cs
│   ├── appsettings.json
│   ├── appsettings.Production.json
│   ├── Rewrite/
│   └── Tds/
├── Queryeasy.Proxy.Tests/
└── tools/
```

В репозитории нет `.sln`-файла. Сборка, запуск и тесты выполняются напрямую через `.csproj`.

## Требования

- .NET SDK с поддержкой `net10.0`.
- Доступный Microsoft SQL Server.
- SQL-клиент или приложение, которое можно направить на адрес прокси.

Встроенные значения по умолчанию слушают `127.0.0.1:11433` и перенаправляют трафик на `127.0.0.1:1433`. Если рядом с запуском есть `appsettings.json`, он имеет приоритет и может задавать другие `TargetHost`, `TargetPort`, `Mode` и rewrite rules.

## Быстрый старт

Сборка проекта:

```powershell
dotnet build .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
```

Запуск с `appsettings.json` из рабочей директории, если файл существует:

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
```

После запуска в консоли появится сообщение вида:

```text
2026-06-22T00:00:00.0000000+00:00 [Info] MSSQL proxy listening on 127.0.0.1:11433, forwarding to 127.0.0.1:1433.
2026-06-22T00:00:00.0000000+00:00 [Info] Press Ctrl+C to stop.
```

Теперь SQL-клиент нужно подключать не к SQL Server напрямую, а к адресу прокси:

```text
Server=127.0.0.1,11433
```

Прокси установит отдельное соединение с реальным SQL Server по адресу `TargetHost:TargetPort` из конфигурации.

Остановка выполняется через `Ctrl+C`.

## Запуск с другим конфигом

Путь к JSON-конфигурации можно передать первым аргументом:

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.PassThrough.json
```

Или:

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.RequirePlainText.json
```

Если аргумент не передан, приложение ищет `appsettings.json` сначала в текущей рабочей директории, затем рядом с собранным исполняемым файлом. Если ни один файл не найден, используются встроенные defaults из `ProxyOptions`.

Если файл конфигурации не найден или в нем нет секции `Proxy`, приложение запускается со значениями по умолчанию.

## Публикация

Опубликовать Release-сборку можно командой:

```powershell
dotnet publish .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -c Release
```

Запуск опубликованного приложения:

```powershell
.\Queryeasy.Proxy.exe
```

Запуск опубликованного приложения с явным конфигом:

```powershell
.\Queryeasy.Proxy.exe .\appsettings.json
```

Важно: в `.csproj` настроено автоматическое копирование `appsettings.json` и `appsettings.Production.json`. Файлы `appsettings.PassThrough.json` и `appsettings.RequirePlainText.json` нужно передавать из исходной директории, копировать рядом с exe вручную или добавить отдельное правило копирования.

## Конфигурация

Основной файл конфигурации:

```text
Queryeasy.Proxy/appsettings.json
```

Структура файла:

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
    "MetricsSummaryIntervalSeconds": 30
  },
  "RewriteRules": []
}
```

JSON-поля десериализуются без учета регистра. Enum-значения задаются строками.

### Proxy

| Параметр | Значение по умолчанию | Описание |
| --- | --- | --- |
| `ListenHost` | `127.0.0.1` | Хост или IP-адрес, на котором прокси принимает подключения клиентов. |
| `ListenPort` | `11433` | TCP-порт прокси. |
| `TargetHost` | `127.0.0.1` | Хост или IP-адрес реального SQL Server. |
| `TargetPort` | `1433` | TCP-порт реального SQL Server. |
| `ConnectTimeoutSeconds` | `10` | Таймаут подключения к целевому SQL Server. |
| `IdleTimeoutMinutes` | `30` | Таймаут простоя чтения в сессии. |
| `BufferSizeBytes` | `81920` | Размер буфера для raw byte forwarding. Минимум - `4096`. |
| `LogLevel` | `Info` | Минимальный уровень логов: `Error`, `Warn`, `Info`, `Debug`, `Trace`. |
| `Mode` | `InspectOnly` | Основной режим работы прокси. |
| `LogPayloadPreview` | `true` | Логировать hex-preview payload для TDS-пакетов. Требует `LogLevel: "Trace"`. |
| `LogSqlText` | `true` | Логировать декодированный SQL-текст. Требует `LogLevel: "Debug"` или `Trace`. |
| `LogRewriteSqlText` | `false` | Логировать SQL после применения rewrite-правила. Требует `LogSqlText` и `LogLevel: "Debug"` или `Trace`. |
| `PayloadPreviewBytes` | `64` | Сколько байт payload показывать в preview. `0` отключает preview. |
| `MaxSqlLogChars` | `4000` | Максимальная длина SQL-текста в логе. `0` отключает усечение. |
| `RewriteFailureBehavior` | `FailOpen` | Что делать при ошибке rewrite. |
| `PreLoginEncryptionMode` | `TryDisable` | Как обрабатывать TDS PreLogin ENCRYPTION. |
| `FailIfEncryptionRequired` | `false` | Завершать сессию, если после PreLogin обнаружен raw TLS. |
| `LogPreLoginOptions` | `true` | Логировать значение ENCRYPTION в PreLogin-пакетах клиента и сервера. Требует `LogLevel: "Debug"` или `Trace`. |
| `MaxConcurrentSessions` | `500` | Максимальное количество одновременных proxy sessions. |
| `MaxInspectableMessageBytes` | `1048576` | Максимальный payload TDS message для inspect/rewrite. При превышении сообщение пересылается без inspect/rewrite. |
| `MaxRewriteSqlChars` | `65536` | Максимальная длина SQL-текста, который можно передать в rewrite engine. `0` отключает лимит. |
| `RejectWhenOverloaded` | `true` | Если session limit исчерпан, новое подключение закрывается сразу. Если `false`, accept loop ждет свободный слот. |
| `MetricsSummaryIntervalSeconds` | `30` | Интервал summary-лога метрик. `0` отключает periodic summary. |

Проверка конфигурации выполняется при запуске. Порты должны быть в диапазоне `0..65535`, хосты не должны быть пустыми, таймауты должны быть больше нуля, `BufferSizeBytes` должен быть не меньше `4096`, `MaxConcurrentSessions` должен быть больше нуля, `MaxInspectableMessageBytes` должен быть не меньше размера TDS header, а числовые лимиты логов/метрик не должны быть отрицательными.

## Режимы работы

### InspectOnly

Встроенный режим по умолчанию. Прокси анализирует клиентский TDS-трафик и отправляет SQL Server исходные пакеты без изменений. Детальные TDS/SQL логи появятся только при достаточно подробном `LogLevel`.

### ForwardOnly

Значение присутствует в enum `ProxyMode`, но в текущей реализации отдельной ветки для него нет. На практике оно ведет себя как режим без rewrite: SQL Batch не переписывается, пакеты пересылаются дальше после обработки pipeline.

### DryRun

Прокси применяет правила rewrite к декодированному SQL Batch или RPC `sp_executesql` только для проверки. Если правило совпало, это логируется, но на SQL Server отправляются исходные TDS-пакеты.

Используйте этот режим перед `Rewrite`, чтобы убедиться, что правила срабатывают на нужных запросах.

### Rewrite

Прокси применяет включенные правила, которые реально изменили SQL Batch или RPC `sp_executesql`, пересобирает TDS-пакеты и отправляет на SQL Server измененный payload.

Для RPC rewrite сейчас поддерживается `sp_executesql`: изменение SQL-текста, значения параметра и типа параметра. Для смены `datetime2(3)` на `datetime2(0)` обновляется и бинарное TDS type info, и строка объявления параметров `@params`.

## Поведение при ошибке rewrite

`RewriteFailureBehavior` определяет, что делать при ошибке правила, например при некорректном regex:

| Значение | Поведение |
| --- | --- |
| `FailOpen` | Ошибка логируется, на SQL Server отправляется исходный SQL. |
| `FailClosed` | Сессия завершается ошибкой, исходный SQL не отправляется дальше. |

## PreLogin и шифрование

SQL-инспекция и rewrite требуют plaintext TDS. Если клиент и сервер переходят на TLS, SQL-текст становится недоступен прокси.

`PreLoginEncryptionMode` управляет обработкой ENCRYPTION-опции в TDS PreLogin:

| Значение | Поведение |
| --- | --- |
| `PassThrough` | PreLogin пересылается без попытки изменить ENCRYPTION. Если `LogPreLoginOptions` выключен, PreLogin вообще не обрабатывается специальной логикой. |
| `TryDisable` | Прокси пытается заменить ENCRYPTION на `EncryptNotSupported` в PreLogin-пакетах клиента и сервера. Если после этого все равно начинается raw TLS, прокси переходит в байтовое проксирование. |
| `RequirePlainText` | Прокси также выставляет `EncryptNotSupported`, но при обнаружении raw TLS завершает сессию ошибкой. |

Дополнительный флаг `FailIfEncryptionRequired` включает fail-closed поведение при raw TLS независимо от выбранного `PreLoginEncryptionMode`.

В репозитории есть два готовых варианта конфигурации:

- `Queryeasy.Proxy/appsettings.PassThrough.json` - оставляет PreLogin ENCRYPTION без изменений.
- `Queryeasy.Proxy/appsettings.RequirePlainText.json` - требует plaintext TDS и завершает сессию, если клиент или сервер все равно включили TLS.

## RewriteRules

Правила rewrite задаются массивом `RewriteRules` в корне JSON-конфигурации. Поддерживаются два формата:

- legacy `Find`/`Replace` для простого SQL Batch rewrite;
- новый формат `Scope` + `When` + `Actions` для SQL Batch и RPC `sp_executesql`.

Поля правила:

| Поле | Значение по умолчанию | Описание |
| --- | --- | --- |
| `Name` | `Unnamed` | Имя правила, которое выводится в логах. |
| `Enabled` | `true` | Включает или отключает правило. |
| `Scope` | `Any` | Где применять правило: `Any`, `SqlBatch`, `RpcSpExecuteSql`. |
| `When` | пустое условие | Условия срабатывания правила. |
| `Actions` | пустой список | Список действий, которые выполняются при совпадении. |
| `MatchType`, `Find`, `Replace`, `IgnoreCase` | legacy fields | Старый формат для одного `ReplaceSql`-действия. |

Поля `When`:

| Поле | Описание |
| --- | --- |
| `SqlContains` | SQL должен содержать указанную строку. |
| `SqlRegex` | SQL должен совпасть с regex. Regex выполняется с таймаутом 1 секунда. |
| `ParameterExists` | Для `RpcSpExecuteSql` должен существовать параметр с таким именем, например `@P1`. |
| `IgnoreCase` | Игнорировать регистр в `SqlContains`, `SqlRegex` и имени параметра. |

Типы `Actions`:

| Type | Обязательные поля | Что делает |
| --- | --- | --- |
| `ReplaceSql` | `Find` | Меняет SQL-текст через `Contains` или `Regex`. |
| `SetParameterValue` | `Name`, `Value` | Меняет значение параметра `sp_executesql`. |
| `SetParameterType` | `Name`, `SqlType` | Меняет тип параметра `sp_executesql`, например `datetime2(0)`. |

При загрузке конфига включенные правила валидируются: `SetParameterValue` требует `Value`, `SetParameterType` требует `SqlType`, parameter actions требуют `Name`.

### Legacy ReplaceSql

```json
{
  "Name": "RedirectOrders",
  "Enabled": true,
  "MatchType": "Contains",
  "Find": "FROM dbo.Orders",
  "Replace": "FROM dbo.Orders_Debug",
  "IgnoreCase": true
}
```

### Actions Format

```json
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
```

Для `ReplaceSql` действие может использовать `MatchType: "Contains"` или `MatchType: "Regex"`. Для regex включается `RegexOptions.CultureInvariant`; при `IgnoreCase: true` дополнительно включается `RegexOptions.IgnoreCase`.

## Пример конфигурации для DryRun

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

В этом режиме прокси покажет, что правило совпало, но отправит на SQL Server исходный запрос.

## Пример конфигурации для RPC Rewrite

```json
{
  "Proxy": {
    "ListenHost": "127.0.0.1",
    "ListenPort": 11433,
    "TargetHost": "127.0.0.1",
    "TargetPort": 1433,
    "LogLevel": "Debug",
    "Mode": "Rewrite",
    "LogSqlText": true,
    "LogRewriteSqlText": true,
    "PreLoginEncryptionMode": "TryDisable",
    "FailIfEncryptionRequired": false,
    "RewriteFailureBehavior": "FailOpen"
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

Такой вариант меняет scale параметра `@P1` в RPC `sp_executesql`: SQL Profiler должен видеть `@P1 datetime2(0)` вместо `@P1 datetime2(3)`.

## Как это работает

Высокоуровневый поток данных:

```mermaid
flowchart LR
    Client["SQL Client"] --> Proxy["Queryeasy.Proxy"]
    Proxy --> SqlServer["SQL Server"]
    Proxy --> ConsoleLog["Console Log"]
```

Основные компоненты:

- `Program.cs` выбирает файл конфигурации, загружает `ProxyOptions`, валидирует настройки, настраивает `ProxyLog` и запускает сервер до `Ctrl+C`.
- `SqlProxyServer.cs` поднимает `TcpListener`, ограничивает concurrent sessions через `SemaphoreSlim`, принимает клиентские подключения и периодически пишет summary метрик.
- `ProxySession.cs` подключается к целевому SQL Server, выполняет PreLogin-обработку, запускает две задачи копирования и обновляет byte counters по завершении сессии.
- `Tds/PreLogin/TdsPreLoginNegotiator.cs` читает PreLogin-пакеты клиента и сервера, логирует ENCRYPTION и при необходимости меняет его на `EncryptNotSupported`.
- `Tds/TdsClientToServerPipeline.cs` обрабатывает поток `client -> sql`: читает TDS-пакеты, собирает многофрагментные сообщения, логирует SQL Batch, инспектирует RPC и применяет rewrite.
- `Rewrite/SqlRewriter.cs` применяет включенные правила rewrite по порядку.
- `ProxyMetrics.cs` хранит in-process counters по sessions, bytes, rewrite, parse warnings и raw TLS fallback.
- Поток `sql -> client` сейчас копируется как байты без TDS-инспекции.

## Что логируется

Для каждой сессии создается короткий идентификатор, например `[a1b2c3d4]`. Он используется во всех сообщениях этой сессии. Каждая строка лога также содержит UTC timestamp и уровень `Error`, `Warn`, `Info`, `Debug` или `Trace`.

В логах можно увидеть:

- подключение клиента;
- подключение к SQL Server;
- PreLogin ENCRYPTION до и после обработки;
- тип TDS-пакета, status, length и packetId;
- hex-preview payload;
- SQL Batch;
- RPC `sp_executesql`: statement, params declaration и значения параметров;
- результат совпадения rewrite-правил;
- rewrite/encode/parse warnings;
- periodic metrics summary;
- закрытие сессии и количество переданных байтов.

Управление подробностью логов выполняется через `LogLevel`, `LogPayloadPreview`, `PayloadPreviewBytes`, `LogSqlText`, `LogRewriteSqlText` и `MaxSqlLogChars`. Для production defaults SQL/payload logs выключены.

## Ограничения

- RPC rewrite реализован только для `sp_executesql`. Другие RPC Request инспектируются и пересылаются без изменений.
- SQL-текст виден только при plaintext TDS. Если начинается raw TLS, прокси не может декодировать или переписывать SQL.
- В направлении `sql -> client` трафик копируется без инспекции и изменения.
- `ForwardOnly` есть в enum, но не имеет отдельного специализированного пути выполнения.
- В репозитории нет `.sln`; команды сборки должны ссылаться на `.csproj`.
- Автоматические тесты покрывают ключевые helper/rewriter/encoder сценарии, но не заменяют end-to-end проверку с реальным SQL Server.
- Альтернативные конфиги `appsettings.PassThrough.json` и `appsettings.RequirePlainText.json` не копируются в output автоматически.
- Проект не добавляет аутентификацию, авторизацию или TLS-терминацию. Это диагностический прокси, а не защитный шлюз.

## Troubleshooting

### Порт прокси уже занят

Если `ListenPort` занят, измените `ListenPort` в конфигурации или остановите процесс, который уже слушает этот порт.

### SQL Server недоступен

Проверьте `TargetHost`, `TargetPort`, сетевую доступность SQL Server и значение `ConnectTimeoutSeconds`. В логах сессии будет видно, что прокси пытается подключиться к целевому серверу.

### SQL не появляется в логах

Наиболее частая причина - соединение перешло на TLS. Проверьте сообщения про PreLogin и raw TLS. Для попытки получить plaintext TDS используйте `PreLoginEncryptionMode: "TryDisable"`. Если нужно строго запретить TLS, используйте `RequirePlainText` вместе с `FailIfEncryptionRequired: true`.

### Rewrite-правило не срабатывает

Проверьте:

- что `Mode` установлен в `DryRun` или `Rewrite`;
- что правило имеет `Enabled: true`;
- что legacy `Find` не пустой или в новом формате заполнены `When` и `Actions`;
- что `Scope` соответствует входящему сообщению: `SqlBatch` или `RpcSpExecuteSql`;
- что для `RpcSpExecuteSql` имя параметра из `ParameterExists` и `Actions[].Name` совпадает с объявлением в `@params`;
- что регистр и пробелы соответствуют правилу или включен `IgnoreCase`;
- что SQL не превышает `MaxRewriteSqlChars`, а TDS message не превышает `MaxInspectableMessageBytes`;
- что соединение не ушло в raw TLS.

### Regex-правило завершает сессию

Если `RewriteFailureBehavior` установлен в `FailClosed`, ошибка regex завершит сессию. Для диагностики можно временно поставить `FailOpen`, исправить выражение и проверить поведение в `DryRun`.

### Альтернативный конфиг не найден после publish

По умолчанию копируются `appsettings.json` и `appsettings.Production.json`. Для других конфигов передайте путь явно или скопируйте файл рядом с опубликованным exe.

## Рекомендованный порядок проверки rewrite

1. Запустите прокси в `InspectOnly` с `LogLevel: "Debug"` и убедитесь, что SQL Batch виден в логах.
2. Добавьте правило в `RewriteRules`, включите `DryRun` и проверьте, что правило совпадает с нужными запросами.
3. Включите `LogRewriteSqlText`, чтобы увидеть итоговый SQL.
4. Переключите `Mode` на `Rewrite`.
5. Для критичных сценариев используйте `PreLoginEncryptionMode: "RequirePlainText"`, `FailIfEncryptionRequired: true` и `RewriteFailureBehavior: "FailClosed"`.

## Безопасность

Queryeasy может выводить SQL-текст и значения параметров в консоль, если включены `LogSqlText` и достаточно подробный `LogLevel`. SQL-запросы могут содержать персональные данные, токены, параметры поиска, фрагменты бизнес-данных или другую чувствительную информацию.

Не запускайте прокси с подробным логированием в средах, где такие данные нельзя сохранять в консольный вывод, терминальные логи или системы сбора логов.

## Разработка

Полезные команды:

```powershell
dotnet build .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
dotnet test .\Queryeasy.Proxy.Tests\Queryeasy.Proxy.Tests.csproj
dotnet publish .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -c Release
```

## Production hardening

Для production-like запуска используйте `Queryeasy.Proxy/appsettings.Production.json` как отправную точку:

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.Production.json
```

Ключевые значения в production-конфиге:

```json
"Mode": "DryRun",
"RewriteFailureBehavior": "FailOpen",
"LogPayloadPreview": false,
"LogSqlText": false,
"LogRewriteSqlText": false,
"LogLevel": "Info",
"MaxConcurrentSessions": 500,
"MaxInspectableMessageBytes": 1048576,
"MaxRewriteSqlChars": 65536
```

Перед переключением на `Rewrite` прогоните правила в `DryRun` и проверьте метрики в summary-логах. Подробное SQL/payload логирование в production лучше включать только временно и точечно.

### Load harness

В `tools/load-harness.ps1` есть простой PowerShell 7 сценарий для сравнения прямого подключения и подключения через прокси:

```powershell
pwsh .\tools\load-harness.ps1 `
  -ConnectionString "Server=127.0.0.1,11433;Database=testdb;Integrated Security=true;TrustServerCertificate=true" `
  -Query "SELECT 1" `
  -Concurrency 16 `
  -IterationsPerWorker 100
```

Сравните результаты для:

- прямого подключения к SQL Server;
- прокси в `ForwardOnly`;
- прокси в `InspectOnly`;
- прокси в `Rewrite`.

Так как тестовый проект появился, базовая автоматическая проверка:

```powershell
dotnet test .\Queryeasy.Proxy.Tests\Queryeasy.Proxy.Tests.csproj
```

Базовая ручная проверка выглядит так:

1. Запустить SQL Server на `TargetHost:TargetPort`.
2. Запустить Queryeasy.
3. Подключить SQL-клиент к `ListenHost:ListenPort`.
4. Выполнить простой SQL Batch, например `SELECT 1`.
5. При `LogLevel: "Debug"` проверить, что в консоли появились TDS-пакеты и SQL Batch.
6. При необходимости включить `DryRun` или `Rewrite` и проверить правила.
