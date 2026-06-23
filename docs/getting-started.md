# Быстрый старт

> **Для кого:** оператор  
> **Время чтения:** ~5 мин  
> **Что узнаете:** как собрать, запустить прокси и подключить SQL-клиент.

## Требования

- .NET SDK с поддержкой `net10.0`.
- Доступный Microsoft SQL Server.
- SQL-клиент или приложение, которое можно направить на адрес прокси.

## Как это работает

```text
SQL Client  ──►  Queryeasy (ListenHost:ListenPort)  ──►  SQL Server (TargetHost:TargetPort)
                     127.0.0.1:11433                         127.0.0.1:1433
```

Клиент подключается к **прокси**. Прокси устанавливает отдельное TCP-соединение с **реальным SQL Server**. Адреса задаются в конфигурации.

## Сборка

```powershell
dotnet build .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
```

## Запуск

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj
```

Если в рабочей директории есть `appsettings.json`, он загружается автоматически. Встроенные значения по умолчанию: слушать `127.0.0.1:11433`, перенаправлять на `127.0.0.1:1433`.

### Ожидаемый вывод консоли

```text
2026-06-22T00:00:00.0000000+00:00 [Info] MSSQL proxy listening on 127.0.0.1:11433, forwarding to 127.0.0.1:1433.
2026-06-22T00:00:00.0000000+00:00 [Info] Press Ctrl+C to stop.
```

При подключении клиента появятся строки с идентификатором сессии, например `[a1b2c3d4]`.

## Подключение клиента

Направьте строку подключения на адрес прокси:

```text
Server=127.0.0.1,11433
```

Для SQL Server Authentication добавьте `User Id`, `Password` и при необходимости `Database` как обычно. Прокси не проверяет учётные данные — они передаются клиентом на SQL Server через TDS login.

Остановка прокси: `Ctrl+C`.

## Запуск с другим конфигом

Путь к JSON можно передать первым аргументом:

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.Production.json
```

Другие готовые варианты:

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.PassThrough.json
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.RequirePlainText.json
```

### Порядок поиска конфигурации

1. Первый аргумент командной строки (если передан).
2. `appsettings.json` в текущей рабочей директории.
3. `appsettings.json` рядом с исполняемым файлом.
4. Встроенные defaults из `ProxyOptions`.

Если файл не найден или в нём нет секции `Proxy`, используются значения по умолчанию (с предупреждением в логе).

Подробнее о параметрах: [configuration.md](configuration.md).

## Публикация

```powershell
dotnet publish .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -c Release
```

Запуск опубликованного приложения:

```powershell
.\Queryeasy.Proxy.exe
.\Queryeasy.Proxy.exe .\appsettings.json
```

**Важно:** в `.csproj` автоматически копируются только `appsettings.json` и `appsettings.Production.json`. Файлы `appsettings.PassThrough.json` и `appsettings.RequirePlainText.json` нужно передавать явным путём, копировать рядом с exe вручную или добавить правило копирования в проект.

## Базовая ручная проверка

1. Запустите SQL Server на `TargetHost:TargetPort`.
2. Запустите Queryeasy.
3. Подключите SQL-клиент к `ListenHost:ListenPort`.
4. Выполните `SELECT 1`.
5. При `LogLevel: "Debug"` и `LogSqlText: true` в консоли должны появиться TDS-пакеты и SQL Batch.
6. При необходимости включите `DryRun` или `Rewrite` — см. [operating-modes.md](operating-modes.md) и [rewrite-rules.md](rewrite-rules.md).

## См. также

- [Конфигурация](configuration.md)
- [Режимы работы](operating-modes.md)
- [Troubleshooting](troubleshooting.md)
