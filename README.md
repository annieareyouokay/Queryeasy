# Queryeasy

Queryeasy — локальный TCP-прокси для Microsoft SQL Server на уровне протокола TDS (Tabular Data Stream). SQL-клиент подключается к прокси; прокси перенаправляет трафик на реальный SQL Server и при необходимости **инспектирует**, **логирует** и **переписывает** SQL Batch или RPC `sp_executesql` перед отправкой на сервер.

Проект полезен для разработки, отладки и экспериментов с SQL-трафиком: увидеть запросы приложения, проверить поведение клиента при изменённом SQL или безопасно протестировать правила переписывания в режиме DryRun.

## Быстрый старт

**Требования:** .NET SDK с поддержкой `net10.0`, доступный SQL Server.

```powershell
dotnet build .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
```

Подключите SQL-клиент к адресу прокси (по умолчанию):

```text
Server=127.0.0.1,11433
```

Прокси сам установит соединение с SQL Server по `TargetHost:TargetPort` из конфигурации (по умолчанию `127.0.0.1:1433`). Остановка — `Ctrl+C`.

Подробнее: [docs/getting-started.md](docs/getting-started.md).

## Типовые сценарии

| Задача | С чего начать |
| --- | --- |
| Посмотреть SQL, который отправляет приложение | [Режимы работы](docs/operating-modes.md) → `InspectOnly`, [Логирование](docs/logging-and-metrics.md) |
| Протестировать правило rewrite без изменения трафика | [Правила rewrite](docs/rewrite-rules.md) → режим `DryRun` |
| Production-подключение с rewrite (например, 1С / datetime2) | [Конфигурация](docs/configuration.md), [appsettings.Production.json](Queryeasy.Proxy/appsettings.Production.json), [Безопасность](docs/security.md) |

## Структура репозитория

```text
Queryeasy/
├── Queryeasy.Proxy/          # Консольный TDS-прокси
│   ├── Program.cs
│   ├── ProxyOptions.cs
│   ├── ProxySession.cs
│   ├── SqlProxyServer.cs
│   ├── appsettings*.json
│   ├── Rewrite/              # Движок правил переписывания SQL
│   └── Tds/                  # TDS-протокол, PreLogin, sp_executesql
├── Queryeasy.Proxy.Tests/    # xUnit-тесты
├── tools/
│   └── load-harness.ps1      # Нагрузочный бенчмарк
└── docs/                     # Подробная документация
```

В репозитории нет `.sln`-файла. Сборка, запуск и тесты выполняются напрямую через `.csproj`.

## Документация

Полная техническая документация — в папке [docs/](docs/index.md):

**Для операторов:** [Быстрый старт](docs/getting-started.md) · [Конфигурация](docs/configuration.md) · [Режимы](docs/operating-modes.md) · [Шифрование и PreLogin](docs/encryption-and-prelogin.md) · [Правила rewrite](docs/rewrite-rules.md) · [Логи и метрики](docs/logging-and-metrics.md) · [Troubleshooting](docs/troubleshooting.md) · [Безопасность](docs/security.md)

**Для разработчиков:** [Архитектура](docs/architecture.md) · [Разработка](docs/development.md) · [Глоссарий](docs/glossary.md)

## Полезные команды

```powershell
dotnet build .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.Production.json
dotnet test .\Queryeasy.Proxy.Tests\Queryeasy.Proxy.Tests.csproj
dotnet publish .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -c Release
```
