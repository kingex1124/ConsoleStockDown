---
name: console-stockdown-add-service
description: Add or extend a service in the ConsoleStockDown project using the repository's existing .NET console architecture. Use when Codex needs to implement a new data-fetching, parsing, persistence, or processing service in this repo, especially when the work should follow the current Models, Repository, Services, AppSettings, Program.cs DI, DatabaseSchema.md, README.md, structured ILogger logging rules, and Traditional Chinese XML comment patterns.
---

# Console Stockdown Add Service

## Quick Start

1. Read `ConsoleStockDown/AGENTS.md`, `ConsoleStockDown/Doc/DatabaseSchema.md`, `ConsoleStockDown/README.md`, and the most similar existing files under `Models/`, `Repository/`, and `Services/`.
2. Mirror the closest existing implementation before inventing new structure. Prefer patterns already used in `StockService` and `InstitutionalTradeService`.
3. Implement the smallest complete vertical slice: model, repository, service, settings, DI wiring, documents, comments, and validation.

## Architecture Checklist

- `Models/`
  Add or update a model when the new service needs persistent data. Use Linq2DB attributes and Traditional Chinese XML comments on the class and properties.
- `Repository/`
  Add an interface and implementation when the service needs database access. Keep table creation, lookup, delete, and insert logic here instead of in services.
- `Services/`
  Add an interface and implementation for HTTP calls, parsing, date handling, orchestration, and logging. Use `ILogger<T>` with structured logging and do not add `Console.WriteLine`.
- `Configuration/AppSettings.cs` and `appsettings.json`
  Add strongly typed settings for API URLs, feature switches, or manual date overrides. Keep example JSON values valid.
- `Program.cs`
  Register new repositories and services in DI, and wire required settings into constructors.
- `Doc/DatabaseSchema.md`
  Document every new table or new persisted field in Traditional Chinese.
- `README.md`
  Update feature list, execution flow, settings table, and any changed runtime behavior.

## Workflow

### 1. Understand the new service

- Identify the new service's data source, persistence needs, execution order, and date rules.
- Decide whether the feature reuses an existing table or needs a new one.
- Inspect the upstream API shape before coding. Check whether it returns object records, positional arrays, mixed string and number fields, or multiple valid row shapes.
- If the payload is inconsistent, reuse the `JsonElement` parsing pattern from the current project instead of assuming all values are strings.

### 2. Choose names and scope

- Follow the existing naming style:
  - Model: `XxxDaily` or another clear domain noun
  - Repository: `IXxxRepository` / `XxxRepository`
  - Service: `IXxxService` / `XxxService`
  - Async methods end with `Async`
- Keep new code inside the current folders unless the repository already established a different pattern.
- If more than one design is possible, prefer the one most similar to the closest existing feature.

### 3. Implement persistence

- Create the model first when a new table is required.
- Add table initialization through the relevant repository with `CreateTable<T>(tableOptions: TableOptions.CreateIfNotExists)`.
- Add only the repository methods needed by the workflow, such as:
  - initialize database/table
  - get latest trade date
  - get prior trade date
  - get one record by key
  - delete by trade date
  - insert a collection of rows

### 4. Implement the service

- Keep HTTP requests, parsing, date normalization, and orchestration in `Services/`.
- Normalize stored dates to `yyyy-MM-dd`.
- If the upstream API supports manual date override, expose it through `AppSettings`.
- When replacing data for one trade date, delete that date first and then insert the newly parsed rows.
- Log the key milestones:
  - initialization
  - selected target date
  - API URL
  - record count before insert
  - completion
- Inject `ILogger<TService>` through the constructor whenever a service has runtime behavior worth tracing.
- Prefer structured logging placeholders such as `{TradeDate}`, `{ApiUrl}`, `{Count}`, and `{StockCode}` instead of string interpolation.
- Use log levels consistently:
  - `LogInformation` for normal workflow milestones
  - `LogWarning` for recoverable anomalies such as missing prior data or skipped records
  - `LogError` for exceptions or failed end states that should be surfaced
  - `LogDebug` only for secondary details that are useful during troubleshooting
- Skip noisy warnings unless the skipped records are truly unexpected. If a known alternate row format exists, parse it instead of logging repeated failures.

### 5. Update wiring and documents

- Register the new repository and service in `Program.cs`.
- If the service should run inside the main workflow, place it in the correct execution order and keep dependencies explicit.
- Update `DatabaseSchema.md` and `README.md` in the same change. Do not leave schema or runtime behavior undocumented.

### 6. Add comments in project style

- Add Traditional Chinese XML comments to all new or edited model, repository, and service classes, interfaces, methods, and important properties.
- Include Traditional Chinese XML comments on important private helper methods when they contain non-obvious parsing, date conversion, or mapping behavior.
- Keep comments short and practical. Explain purpose, data meaning, and behavior; do not restate trivial syntax.

### 7. Validate

- Run `dotnet build ConsoleStockDown/ConsoleStockDown.csproj --no-restore` after edits.
- If the environment allows network access, run the project once and inspect logs for:
  - parse failures
  - unexpected date mismatches
  - repeated duplicate inserts
- If sandbox, Windows policy, or network restrictions block execution, say so clearly in the final response.

## Parsing Notes

- Prefer `List<Dictionary<string, JsonElement>>` when the API returns named fields.
- Prefer `List<List<JsonElement>>` when the API returns positional arrays or mixes strings and numbers.
- If the upstream payload has more than one valid row shape, explicitly support each known column count instead of treating one as malformed.
- Preserve the raw API date in storage when it helps debugging or later reconciliation.

## Done Criteria

- The new feature compiles.
- Required model and database access are in place.
- DI and settings are wired.
- `README.md` and `DatabaseSchema.md` are updated.
- Structured `ILogger<T>` logging is present in the new or updated runtime flow.
- Traditional Chinese XML comments are present on touched model, service, and repository code.
