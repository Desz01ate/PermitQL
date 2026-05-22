# PermitQL

PermitQL is a governed database access layer and MCP server for SQL databases. It validates incoming SQL, rewrites queries to enforce rules such as row limits and filters, and exposes both HTTP and stdio transports for tool-based clients.

License: GNU Affero General Public License v3.0 or later. See [LICENSE](LICENSE) for the repository notice.

The repository contains:

- `PermitQL/` - the core library for parsing, validating, rewriting, and executing governed queries
- `PermitQL.Server/` - the MCP server and HTTP API host
- `PermitQL.Tests/` - xUnit coverage for parsing, validation, rewriting, rules, metadata, and server behavior
- `Rules/example_rules.yaml` - a starter rule set for the sample database
- `init/` - schema and seed scripts used by the local Postgres container

## What It Does

- Validates SQL before execution
- Rewrites supported queries to enforce governed behavior
- Loads rule sets from YAML
- Discovers database metadata for PostgreSQL and SQLite
- Exposes MCP tools for querying and describing governed databases
- Exposes HTTP endpoints for query execution and database description

## Prerequisites

- .NET 10 SDK
- Docker and Docker Compose, if you want to run the sample Postgres database

## Quick Start

1. Restore and build the solution:

   ```bash
   dotnet restore PermitQL.sln
   dotnet build PermitQL.sln
   ```

2. Start the sample Postgres database:

   ```bash
   docker compose up
   ```

3. Run the server:

   ```bash
   dotnet run --project PermitQL.Server
   ```

The default configuration in `PermitQL.Server/appsettings.json` points to the local Postgres instance created by `docker compose`.

## Installation

Install the global tool from `nuget.org`:

```bash
dotnet tool install --global permitql
```

Update an existing installation:

```bash
dotnet tool update --global permitql
```

Tagged releases also publish standalone server archives for:

- `linux-x64`
- `win-x64`
- `osx-arm64`

Download those binaries from the repository's GitHub Releases page when you do not want to install through the .NET tool feed.

## Configuration

Server settings are read from the `PermitQL` configuration section.

Required values:

- `RulesDirectory` - path to the directory containing YAML rule sets
- `ConnectionString` - database connection string
- `Provider` - `postgresql` or `sqlite`

You can provide configuration through:

- `PermitQL.Server/appsettings.json`
- `appsettings.{Environment}.json`
- environment variables, for example `PermitQL__ConnectionString`
- `PermitQL_CONFIG_JSON`

Example:

```json
{
  "PermitQL": {
    "RulesDirectory": ".",
    "ConnectionString": "Host=localhost;Database=mydb;Username=myuser;Password=mypassword",
    "Provider": "postgresql"
  }
}
```

## Running The Server

The server supports two modes:

- HTTP transport, which is the default
- stdio transport, which is intended for local MCP client integration

### HTTP Mode

Start the server normally:

```bash
dotnet run --project PermitQL.Server
```

Available endpoints:

- `POST /api/query` - execute a governed query
- `GET /api/databases` - list available rule set keys
- `GET /api/databases/{key}` - return a governed database description as JSON

### Stdio Mode

Run the server with the `serve --stdio` arguments:

```bash
dotnet run --project PermitQL.Server -- serve --stdio
```

## CLI

The server project exposes two verbs:

- `serve` - run the MCP server
- `discover` - inspect a database and write discovered schema metadata to a file

Examples:

```bash
permitql discover --output discovered_schema.yaml
permitql serve
permitql serve --stdio
dotnet run --project PermitQL.Server -- discover --output discovered_schema.yaml
dotnet run --project PermitQL.Server -- serve
dotnet run --project PermitQL.Server -- serve --stdio
```

## MCP Tools

The server registers these MCP tools:

- `query` - execute a governed SQL query
- `list_databases` - list the available rule set keys
- `describe_database` - describe the governed schema, capabilities, limits, relationships, indexes, and statistics

## Sample Rules And Database

The bundled sample rule file governs a database named `mydb` and exposes tables in the `public` schema.

To try the sample setup:

1. Start the Postgres container with `docker compose up`
2. Use the schema and seed scripts in `init/` to populate the sample database
3. Point the server at `Rules/example_rules.yaml` or another rules directory

## Testing

Run the full test suite:

```bash
dotnet test PermitQL.sln
```

The tests cover:

- SQL parsing
- query validation
- query rewriting
- YAML rules loading
- metadata resolution for PostgreSQL and SQLite
- server startup and MCP tool behavior

## Notes

- `appsettings*.json` files are intentionally excluded from publish output.
- The server uses the configured database provider to select PostgreSQL or SQLite behavior.
- Keep rule-set filenames and schema/table names aligned with the identifiers you expose in YAML.
