# Repository Guidelines

## Project Structure & Module Organization
- `PermitQL/` contains the core library: query parsing, validation, rewriting, rules, and shared models.
- `PermitQL.Server/` hosts the MCP/server entrypoint, CLI wiring, and runtime configuration.
- `PermitQL.Tests/` contains xUnit tests grouped by feature area, for example `Server/`, `Rules/`, and `Rewriting/`.
- `Rules/example_rules.yaml` is the starter policy file for database access rules.
- `init/01_schema.sql` and `init/02_seed.sql` define the local database bootstrap used by `docker-compose.yml`.

## Build, Test, and Development Commands
- `dotnet restore PermitQL.sln` restores all centrally managed packages.
- `dotnet build PermitQL.sln` builds the library, server, and tests with warnings treated as errors.
- `dotnet test PermitQL.sln` runs the full xUnit suite.
- `dotnet run --project PermitQL.Server` starts the server locally for manual checks.
- `docker compose up` brings up the supporting local database setup when you need the init scripts.

## Coding Style & Naming Conventions
- Use 4-space indentation, file-scoped namespaces, nullable reference types, and implicit usings.
- Prefer PascalCase for types, methods, and public members; camelCase for locals and parameters.
- Keep async methods suffixed with `Async`.
- Match the existing style: small focused classes, guard clauses, and `required` init properties where data is mandatory.

## Testing Guidelines
- Tests use xUnit with NSubstitute for mocks.
- Name test files by feature area and test methods by behavior, for example `QueryValidatorTests` and `ReturnsValidJson_WithExpectedTopLevelKeys`.
- Add regression tests alongside behavior changes, especially for SQL rewriting, validation, and metadata resolution.
- Run `dotnet test PermitQL.sln` before opening a PR.

## Commit & Pull Request Guidelines
- Recent commits use short, imperative summaries that mention the affected area, such as `Replace markdown describe_database output with governed JSON`.
- Keep commits focused and reviewable; avoid mixing unrelated refactors with behavior changes.
- PRs should include a clear summary, the verification performed, and notes about any schema, rules, or startup changes.
- Include sample output or screenshots only when user-visible behavior changes.

## Security & Configuration Tips
- Do not commit secrets or machine-specific settings. `appsettings*.json` is excluded from publish output.
- Treat `Rules/` and `init/` scripts as source-controlled configuration; update them deliberately and test the full flow when they change.
