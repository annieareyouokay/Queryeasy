# Правила rewrite

> **Для кого:** оператор  
> **Время чтения:** ~12 мин  
> **Что узнаете:** как задавать правила переписывания SQL Batch и RPC `sp_executesql`.

## Где задаются правила

Массив **`RewriteRules`** в **корне** JSON-конфига (не внутри `Proxy`). Поддерживаются два формата:

- **Legacy** — `Find` / `Replace` для простого SQL Batch rewrite.
- **Новый** — `Scope` + `When` + `Actions` для SQL Batch и RPC `sp_executesql`.

Движок: [SqlRewriter.cs](../Queryeasy.Proxy/Rewrite/SqlRewriter.cs).

## Ограничение RPC

Rewrite RPC реализован **только для `sp_executesql`**. Другие RPC Request инспектируются (имя процедуры) и пересылаются без изменений.

## Поля правила

| Поле | По умолчанию | Описание |
| --- | --- | --- |
| `Name` | `Unnamed` | Имя правила в логах. |
| `Enabled` | `true` | Включить/отключить правило. |
| `Scope` | `Any` | `Any`, `SqlBatch`, `RpcSpExecuteSql`. |
| `When` | пустое | Условия срабатывания. |
| `Actions` | пустой список | Действия при совпадении. |
| `MatchType`, `Find`, `Replace`, `IgnoreCase` | legacy | Старый формат одного `ReplaceSql`. |

## Поля When

| Поле | Описание |
| --- | --- |
| `SqlContains` | SQL должен содержать указанную строку. |
| `SqlRegex` | SQL должен совпасть с regex (таймаут 1 с). |
| `ParameterExists` | Для `RpcSpExecuteSql` — параметр с таким именем, например `@P1`. |
| `ParameterNameRegex` | Имя из `@params` совпадает с regex, например `@P\\d+`. |
| `ParameterType` | Тип из `@params`, например `datetime2(3)`. Требует `ParameterExists` или `ParameterNameRegex`. |
| `IgnoreCase` | Игнорировать регистр в `SqlContains`, `SqlRegex`, `ParameterNameRegex` и имени параметра. |

## Типы Actions

| Type | Обязательные поля | Что делает |
| --- | --- | --- |
| `ReplaceSql` | `Find` | Меняет SQL через `Contains` или `Regex`. |
| `SetParameterValue` | `Name`, `Value` | Меняет значение параметра `sp_executesql`. |
| `SetParameterType` | `SqlType`, `Name` (опционально) | Меняет тип, например `datetime2(0)`. Без `Name` — ко всем параметрам, прошедшим фильтр `When`. |

При загрузке валидируются: `SetParameterValue` требует `Value`, `SetParameterType` — `SqlType`, parameter actions — `Name` или filter в `When`, `ParameterType` — `ParameterExists` или `ParameterNameRegex`.

Для `ReplaceSql`: `MatchType: "Contains"` или `"Regex"`. Regex с `RegexOptions.CultureInvariant`; при `IgnoreCase: true` добавляется `RegexOptions.IgnoreCase`.

---

## Рецепт 1: Redirect таблицы (legacy)

**До:** запросы к `dbo.Orders`  
**После:** запросы к `dbo.Orders_Debug`

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

Подходит для SQL Batch. Для RPC используйте `Scope: "RpcSpExecuteSql"` и формат When/Actions с `ReplaceSql` в `Actions`.

---

## Рецепт 2: Смена scale `@P1` (RPC)

**До:** `@P1 datetime2(3)` в `@params`  
**После:** `@P1 datetime2(0)` — видно в SQL Profiler

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

---

## Рецепт 3: Все `@Pn` с `datetime2(3)` для таблицы 1С

**Сценарий:** запросы к `dbo._Reference47`, параметры `@P1`, `@P2`, … с типом `datetime2(3)`.

**До:**

```sql
-- @stmt
SELECT T1._Description FROM dbo._Reference47 T1 WHERE T1._Fld58 = @P1 AND T1._Date = @P2
-- @params
@P1 datetime2(3), @P2 datetime2(3), @P3 int
```

**После rewrite:** `@P1` и `@P2` → `datetime2(0)`; `@P3 int` не затрагивается.

```json
{
  "Name": "Reference47_DateTime2Scale3",
  "Enabled": true,
  "Scope": "RpcSpExecuteSql",
  "When": {
    "SqlContains": "dbo._Reference47",
    "ParameterNameRegex": "@P\\d+",
    "ParameterType": "datetime2(3)"
  },
  "Actions": [
    {
      "Type": "SetParameterType",
      "SqlType": "datetime2(0)"
    }
  ]
}
```

Это правило используется в [appsettings.Production.json](../Queryeasy.Proxy/appsettings.Production.json).

---

## Actions Format (полный пример)

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

## Связь с режимами

- **`DryRun`** — совпадение логируется, трафик не меняется.
- **`Rewrite`** — изменения отправляются на SQL Server.

См. [operating-modes.md](operating-modes.md) и [troubleshooting.md](troubleshooting.md), если правило не срабатывает.

## См. также

- [Конфигурация](configuration.md)
- [Глоссарий](glossary.md)
