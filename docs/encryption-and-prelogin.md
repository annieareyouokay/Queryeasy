# Шифрование и PreLogin

> **Для кого:** оператор  
> **Время чтения:** ~7 мин  
> **Что узнаете:** почему inspect/rewrite требуют plaintext TDS и как настроить PreLogin.

## Зачем это важно

SQL-инспекция и rewrite работают только с **plaintext TDS**. Если клиент и SQL Server переходят на **TLS**, SQL-текст в потоке зашифрован — прокси видит только байты и не может декодировать или переписывать запросы.

На этапе **PreLogin** (до login) клиент и сервер согласуют опцию **ENCRYPTION**. Queryeasy может попытаться изменить эту опцию или пропустить PreLogin без изменений.

## PreLoginEncryptionMode

| Значение | Поведение |
| --- | --- |
| `PassThrough` | PreLogin пересылается без изменения ENCRYPTION. Если `LogPreLoginOptions` выключен, PreLogin может не обрабатываться специальной логикой. |
| `TryDisable` | Прокси заменяет ENCRYPTION на `EncryptNotSupported` в PreLogin клиента и сервера. Если после этого всё равно начинается raw TLS — переход в обычное байтовое проксирование (fallback). |
| `RequirePlainText` | Также выставляет `EncryptNotSupported`, но при обнаружении raw TLS **завершает сессию ошибкой**. |

Реализация: [TdsPreLoginNegotiator.cs](../Queryeasy.Proxy/Tds/PreLogin/TdsPreLoginNegotiator.cs).

## FailIfEncryptionRequired

Дополнительный флаг в секции `Proxy`. При `true` сессия завершается при raw TLS **независимо** от `PreLoginEncryptionMode` — fail-closed для сценариев, где rewrite обязателен.

## Поведение при raw TLS

В [TdsClientToServerPipeline.cs](../Queryeasy.Proxy/Tds/TdsClientToServerPipeline.cs) при `RawTlsDetectedException`:

- **`RequirePlainText`** или **`FailIfEncryptionRequired: true`** → сессия завершается с ошибкой: *«Raw TLS stream detected… fail-closed mode is enabled»*.
- Иначе → raw byte forwarding, метрика `raw_tls_fallbacks` увеличивается.

## Готовые конфигурации

| Файл | Назначение |
| --- | --- |
| [appsettings.PassThrough.json](../Queryeasy.Proxy/appsettings.PassThrough.json) | PreLogin ENCRYPTION без изменений |
| [appsettings.RequirePlainText.json](../Queryeasy.Proxy/appsettings.RequirePlainText.json) | Требует plaintext TDS; сессия завершается при TLS |

Запуск:

```powershell
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.PassThrough.json
dotnet run --project .\Queryeasy.Proxy\Queryeasy.Proxy.csproj -- .\Queryeasy.Proxy\appsettings.RequirePlainText.json
```

Эти файлы **не** копируются в output при publish — передайте путь явно или скопируйте рядом с exe.

## Диагностика

Если SQL не появляется в логах:

1. Проверьте сообщения про PreLogin ENCRYPTION и raw TLS.
2. Для попытки plaintext: `PreLoginEncryptionMode: "TryDisable"`.
3. Для строгого запрета TLS: `RequirePlainText` + `FailIfEncryptionRequired: true`.

На клиенте иногда помогает `Encrypt=false` в строке подключения (если драйвер поддерживает).

Подробнее: [troubleshooting.md](troubleshooting.md).

## См. также

- [Глоссарий: PreLogin, Plaintext TDS](glossary.md)
- [Логирование](logging-and-metrics.md)
- [Режимы работы](operating-modes.md)
