# Describe Database Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current markdown `describe_database` output with a governed JSON metadata contract that includes richer schema metadata, relationship directionality, capability flags, and lightweight statistics.

**Architecture:** Extend `IDataAccessor` with focused schema-introspection methods and provider-backed resolver collaborators, then have `PermitQLTools.DescribeDatabase` assemble and filter a JSON response from those sources. Keep query/validator capability disclosure separate internally, merge it at the tool boundary, and preserve strict ruleset-based filtering for all emitted metadata.

**Tech Stack:** C# / .NET 10, xUnit, NSubstitute, ADO.NET, System.Text.Json, existing `PermitQL` and `PermitQL.Server` projects

---

## File Structure

- Modify: `../PermitQL/Abstractions/IDataAccessor.cs` — add focused metadata methods for constraints, inbound/outbound FKs, indexes, statistics, and capabilities
- Modify: `../PermitQL/Abstractions/IPermitQLFactory.cs` — add factory parameters for richer accessor dependencies
- Modify: `../PermitQL/Models/QueryResult.cs` — keep query models and add or split out richer schema-introspection records
- Create: `../PermitQL/Models/SchemaMetadata.cs` — dedicated records for column metadata, constraints, relationships, indexes, statistics, and capability statuses
- Modify: `../PermitQL/Data/AdoNetDataAccessor.cs` — orchestrate richer metadata calls through focused resolvers
- Create: `../PermitQL/Abstractions/IConstraintResolver.cs`
- Create: `../PermitQL/Abstractions/IRelationshipResolver.cs`
- Create: `../PermitQL/Abstractions/IIndexResolver.cs`
- Create: `../PermitQL/Abstractions/IStatisticsResolver.cs`
- Create: `../PermitQL/Abstractions/IProviderCapabilityResolver.cs`
- Create: `../PermitQL/Data/NullConstraintResolver.cs`
- Create: `../PermitQL/Data/NullRelationshipResolver.cs`
- Create: `../PermitQL/Data/NullIndexResolver.cs`
- Create: `../PermitQL/Data/NullStatisticsResolver.cs`
- Create: `../PermitQL/Data/NullProviderCapabilityResolver.cs`
- Modify: `../PermitQL/PermitQLFactory.cs` — construct accessor with resolver dependencies
- Modify: `../PermitQL/DependencyInjection.cs` — thread resolver dependencies into service registration
- Delete: `ForeignKeyResolverFactory.cs` — replace FK-only startup wiring with `ProviderMetadataResolverFactory.cs`
- Create: `Implementations/MetadataResolvers/PostgresConstraintResolver.cs`
- Create: `Implementations/MetadataResolvers/PostgresRelationshipResolver.cs`
- Create: `Implementations/MetadataResolvers/PostgresIndexResolver.cs`
- Create: `Implementations/MetadataResolvers/PostgresStatisticsResolver.cs`
- Create: `Implementations/MetadataResolvers/SqliteConstraintResolver.cs`
- Create: `Implementations/MetadataResolvers/SqliteRelationshipResolver.cs`
- Create: `Implementations/MetadataResolvers/SqliteIndexResolver.cs`
- Create: `Implementations/MetadataResolvers/SqliteStatisticsResolver.cs`
- Create: `ProviderMetadataResolverFactory.cs` — central dialect-based resolver wiring for server startup
- Create: `ValidatorCapabilityDescriptor.cs` — server-owned capability declaration for validator behavior
- Modify: `Tools/PermitQLTools.cs` — emit JSON-only response, merge capabilities, enforce filtering and omission rules
- Modify: `Program.cs` — register richer metadata dependencies and return raw JSON from `/api/databases/{key}`
- Create: `../PermitQL.Tests/Models/SchemaMetadataTests.cs`
- Create: `../PermitQL.Tests/Data/AdoNetDataAccessorMetadataTests.cs`
- Create: `../PermitQL.Tests/Server/DescribeDatabaseToolTests.cs`
- Create: `../PermitQL.Tests/Server/ValidatorCapabilityDescriptorTests.cs`
- Modify: `../PermitQL.Tests/PermitQLFactoryTests.cs`

### Task 1: Define shared metadata models and accessor surface

**Files:**
- Create: `../PermitQL/Models/SchemaMetadata.cs`
- Modify: `../PermitQL/Abstractions/IDataAccessor.cs`
- Modify: `../PermitQL/Models/QueryResult.cs`
- Test: `../PermitQL.Tests/Models/SchemaMetadataTests.cs`

- [ ] **Step 1: Write the failing model/contract tests**

Create `../PermitQL.Tests/Models/SchemaMetadataTests.cs`:

```csharp
namespace PermitQL.Tests.Models;

using PermitQL.Models;

public sealed class SchemaMetadataTests
{
    [Fact]
    public void ColumnMetadata_CanRepresentDefaultAndGenerationState()
    {
        var column = new SchemaColumnMetadata(
            Name: "id",
            Type: "integer",
            IsNullable: false,
            IsPrimaryKey: true,
            DefaultValue: null,
            IsGenerated: true,
            GenerationKind: GenerationKind.Identity);

        Assert.True(column.IsPrimaryKey);
        Assert.True(column.IsGenerated);
        Assert.Equal(GenerationKind.Identity, column.GenerationKind);
    }

    [Fact]
    public void TableStatistics_UsesNullableApproximateRowCount()
    {
        var stats = new TableStatisticsMetadata(ApproximateRowCount: null, LastAnalyzed: null);

        Assert.Null(stats.ApproximateRowCount);
        Assert.Null(stats.LastAnalyzed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ../PermitQL.Tests --filter SchemaMetadataTests -v minimal`
Expected: FAIL with missing `SchemaColumnMetadata`, `GenerationKind`, and `TableStatisticsMetadata` types.

- [ ] **Step 3: Add dedicated schema metadata records**

Create `../PermitQL/Models/SchemaMetadata.cs`:

```csharp
namespace PermitQL.Models;

public enum CapabilitySupport
{
    Supported,
    Unsupported,
    Unknown,
}

public enum GenerationKind
{
    None,
    Identity,
    AutoIncrement,
    Computed,
    Unknown,
}

public sealed record SchemaColumnMetadata(
    string Name,
    string Type,
    bool IsNullable,
    bool IsPrimaryKey,
    string? DefaultValue,
    bool IsGenerated,
    GenerationKind GenerationKind);

public sealed record UniqueConstraintMetadata(string Name, IReadOnlyList<string> Columns);

public sealed record CheckConstraintMetadata(string Name, string Expression);

public sealed record TableConstraintMetadata(
    IReadOnlyList<UniqueConstraintMetadata> Unique,
    IReadOnlyList<CheckConstraintMetadata> Check);

public sealed record ForeignKeyMetadata(
    string ConstraintName,
    string FromSchema,
    string FromTable,
    string FromColumn,
    string ToSchema,
    string ToTable,
    string ToColumn,
    string? OnDelete,
    string? OnUpdate);

public sealed record TableIndexMetadata(string Name, IReadOnlyList<string> Columns, bool IsUnique);

public sealed record TableStatisticsMetadata(long? ApproximateRowCount, DateTimeOffset? LastAnalyzed);

public sealed record QueryCapabilityMetadata(
    CapabilitySupport Ctes,
    CapabilitySupport Subqueries,
    CapabilitySupport DerivedTables,
    CapabilitySupport WindowFunctions,
    CapabilitySupport Mutations,
    IReadOnlyList<string> Notes);
```

- [ ] **Step 4: Extend the accessor contract**

Update `../PermitQL/Abstractions/IDataAccessor.cs` so it exposes the new metadata methods:

```csharp
public interface IDataAccessor
{
    ValueTask<IReadOnlyList<ColumnDefinition>> GetColumnDefinitionAsync(string query, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SchemaColumnMetadata>> GetTableColumnsAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<TableConstraintMetadata> GetTableConstraintsAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ForeignKeyMetadata>> GetOutboundForeignKeysAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ForeignKeyMetadata>> GetInboundForeignKeysAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TableIndexMetadata>> GetTableIndexesAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<TableStatisticsMetadata> GetTableStatisticsAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<QueryCapabilityMetadata> GetQueryCapabilitiesAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<object?[]> QueryAsync(string query, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Keep query-result models focused**

Trim `../PermitQL/Models/QueryResult.cs` back to query-only types:

```csharp
namespace PermitQL.Models;

public record ColumnDefinition(int Index, string Name, string Type, bool IsNullable, bool IsKey = false);

public record QueryResult(IReadOnlyList<ColumnDefinition> Columns, IReadOnlyList<object?[]> Rows);
```

- [ ] **Step 6: Run the model tests to verify they pass**

Run: `dotnet test ../PermitQL.Tests --filter SchemaMetadataTests -v minimal`
Expected: PASS with `2 passed`.

- [ ] **Step 7: Commit**

```bash
git add ../PermitQL/Abstractions/IDataAccessor.cs ../PermitQL/Models/QueryResult.cs ../PermitQL/Models/SchemaMetadata.cs ../PermitQL.Tests/Models/SchemaMetadataTests.cs
git commit -m "Define schema metadata contracts for describe_database"
```

### Task 2: Add resolver abstractions, null implementations, and accessor orchestration

**Files:**
- Create: `../PermitQL/Abstractions/IConstraintResolver.cs`
- Create: `../PermitQL/Abstractions/IRelationshipResolver.cs`
- Create: `../PermitQL/Abstractions/IIndexResolver.cs`
- Create: `../PermitQL/Abstractions/IStatisticsResolver.cs`
- Create: `../PermitQL/Abstractions/IProviderCapabilityResolver.cs`
- Create: `../PermitQL/Data/NullConstraintResolver.cs`
- Create: `../PermitQL/Data/NullRelationshipResolver.cs`
- Create: `../PermitQL/Data/NullIndexResolver.cs`
- Create: `../PermitQL/Data/NullStatisticsResolver.cs`
- Create: `../PermitQL/Data/NullProviderCapabilityResolver.cs`
- Modify: `../PermitQL/Data/AdoNetDataAccessor.cs`
- Modify: `../PermitQL/PermitQLFactory.cs`
- Modify: `../PermitQL/Abstractions/IPermitQLFactory.cs`
- Test: `../PermitQL.Tests/Data/AdoNetDataAccessorMetadataTests.cs`
- Modify: `../PermitQL.Tests/PermitQLFactoryTests.cs`

- [ ] **Step 1: Write failing accessor orchestration tests**

Create `../PermitQL.Tests/Data/AdoNetDataAccessorMetadataTests.cs`:

```csharp
namespace PermitQL.Tests.Data;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Data;
using PermitQL.Models;
using NSubstitute;

public sealed class AdoNetDataAccessorMetadataTests
{
    [Fact]
    public async Task GetTableConstraintsAsync_DelegatesToConstraintResolver()
    {
        var connection = Substitute.For<DbConnection>();
        var resolver = Substitute.For<IConstraintResolver>();
        resolver.ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));

        var accessor = new AdoNetDataAccessor(
            () => connection,
            constraintResolver: resolver);

        await accessor.GetTableConstraintsAsync("public", "orders");

        await resolver.Received(1).ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ../PermitQL.Tests --filter AdoNetDataAccessorMetadataTests -v minimal`
Expected: FAIL with missing resolver interfaces and constructor parameters.

- [ ] **Step 3: Define focused resolver interfaces**

Create `../PermitQL/Abstractions/IConstraintResolver.cs` and companion interfaces:

```csharp
namespace PermitQL.Abstractions;

using System.Data.Common;
using Models;

public interface IConstraintResolver
{
    ValueTask<TableConstraintMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default);
}
```

Use the same pattern for:

```csharp
public interface IRelationshipResolver
{
    ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveOutboundAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveInboundAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken = default);
}

public interface IIndexResolver
{
    ValueTask<IReadOnlyList<TableIndexMetadata>> ResolveAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken = default);
}

public interface IStatisticsResolver
{
    ValueTask<TableStatisticsMetadata> ResolveAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken = default);
}

public interface IProviderCapabilityResolver
{
    ValueTask<QueryCapabilityMetadata> ResolveAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Add null resolver implementations**

Create `../PermitQL/Data/NullConstraintResolver.cs` and the other null resolvers with empty/unknown defaults:

```csharp
namespace PermitQL.Data;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class NullConstraintResolver : IConstraintResolver
{
    public static readonly NullConstraintResolver Instance = new();

    public ValueTask<TableConstraintMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new TableConstraintMetadata([], []));
}
```

For capabilities, return:

```csharp
new QueryCapabilityMetadata(
    CapabilitySupport.Unknown,
    CapabilitySupport.Unknown,
    CapabilitySupport.Unknown,
    CapabilitySupport.Unknown,
    CapabilitySupport.Unknown,
    []);
```

- [ ] **Step 5: Update `AdoNetDataAccessor` to orchestrate resolvers**

Modify the constructor and metadata methods in `../PermitQL/Data/AdoNetDataAccessor.cs`:

```csharp
public sealed class AdoNetDataAccessor : IDataAccessor
{
    private readonly Func<DbConnection> _connectionFactory;
    private readonly IConstraintResolver _constraintResolver;
    private readonly IRelationshipResolver _relationshipResolver;
    private readonly IIndexResolver _indexResolver;
    private readonly IStatisticsResolver _statisticsResolver;
    private readonly IProviderCapabilityResolver _capabilityResolver;

    public AdoNetDataAccessor(
        Func<DbConnection> connectionFactory,
        IConstraintResolver? constraintResolver = null,
        IRelationshipResolver? relationshipResolver = null,
        IIndexResolver? indexResolver = null,
        IStatisticsResolver? statisticsResolver = null,
        IProviderCapabilityResolver? capabilityResolver = null)
    {
        _connectionFactory = connectionFactory;
        _constraintResolver = constraintResolver ?? NullConstraintResolver.Instance;
        _relationshipResolver = relationshipResolver ?? NullRelationshipResolver.Instance;
        _indexResolver = indexResolver ?? NullIndexResolver.Instance;
        _statisticsResolver = statisticsResolver ?? NullStatisticsResolver.Instance;
        _capabilityResolver = capabilityResolver ?? NullProviderCapabilityResolver.Instance;
    }

    public async ValueTask<TableConstraintMetadata> GetTableConstraintsAsync(string schema, string table, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        return await _constraintResolver.ResolveAsync(connection, schema, table, cancellationToken);
    }
}
```

Add the remaining methods in the same file:

```csharp
public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> GetOutboundForeignKeysAsync(string schema, string table, CancellationToken cancellationToken = default)
{
    await using var connection = _connectionFactory();
    await connection.OpenAsync(cancellationToken);
    return await _relationshipResolver.ResolveOutboundAsync(connection, schema, table, cancellationToken);
}

public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> GetInboundForeignKeysAsync(string schema, string table, CancellationToken cancellationToken = default)
{
    await using var connection = _connectionFactory();
    await connection.OpenAsync(cancellationToken);
    return await _relationshipResolver.ResolveInboundAsync(connection, schema, table, cancellationToken);
}

public async ValueTask<IReadOnlyList<TableIndexMetadata>> GetTableIndexesAsync(string schema, string table, CancellationToken cancellationToken = default)
{
    await using var connection = _connectionFactory();
    await connection.OpenAsync(cancellationToken);
    return await _indexResolver.ResolveAsync(connection, schema, table, cancellationToken);
}

public async ValueTask<TableStatisticsMetadata> GetTableStatisticsAsync(string schema, string table, CancellationToken cancellationToken = default)
{
    await using var connection = _connectionFactory();
    await connection.OpenAsync(cancellationToken);
    return await _statisticsResolver.ResolveAsync(connection, schema, table, cancellationToken);
}

public ValueTask<QueryCapabilityMetadata> GetQueryCapabilitiesAsync(CancellationToken cancellationToken = default)
    => _capabilityResolver.ResolveAsync(cancellationToken);
```

- [ ] **Step 6: Thread new dependencies through the factory**

Update `../PermitQL/Abstractions/IPermitQLFactory.cs` and `../PermitQL/PermitQLFactory.cs`:

```csharp
IDataAccessor CreateDataAccessor(
    Func<DbConnection> connectionFactory,
    IConstraintResolver? constraintResolver = null,
    IRelationshipResolver? relationshipResolver = null,
    IIndexResolver? indexResolver = null,
    IStatisticsResolver? statisticsResolver = null,
    IProviderCapabilityResolver? capabilityResolver = null);
```

And:

```csharp
public virtual IDataAccessor CreateDataAccessor(
    Func<DbConnection> connectionFactory,
    IConstraintResolver? constraintResolver = null,
    IRelationshipResolver? relationshipResolver = null,
    IIndexResolver? indexResolver = null,
    IStatisticsResolver? statisticsResolver = null,
    IProviderCapabilityResolver? capabilityResolver = null)
    => new AdoNetDataAccessor(
        connectionFactory,
        constraintResolver,
        relationshipResolver,
        indexResolver,
        statisticsResolver,
        capabilityResolver);
```

- [ ] **Step 7: Expand the factory test**

Add to `../PermitQL.Tests/PermitQLFactoryTests.cs`:

```csharp
[Fact]
public void CreateDataAccessor_WithMetadataResolvers_ReturnsAccessor()
{
    var factory = new PermitQLFactory(SqlDialect.PostgreSql);

    var accessor = factory.CreateDataAccessor(
        () => Substitute.For<DbConnection>(),
        Substitute.For<IConstraintResolver>(),
        Substitute.For<IRelationshipResolver>(),
        Substitute.For<IIndexResolver>(),
        Substitute.For<IStatisticsResolver>(),
        Substitute.For<IProviderCapabilityResolver>());

    Assert.IsType<AdoNetDataAccessor>(accessor);
}
```

- [ ] **Step 8: Run targeted tests to verify they pass**

Run: `dotnet test ../PermitQL.Tests --filter "AdoNetDataAccessorMetadataTests|PermitQLFactoryTests" -v minimal`
Expected: PASS with the new accessor orchestration tests green.

- [ ] **Step 9: Commit**

```bash
git add ../PermitQL/Abstractions/IConstraintResolver.cs ../PermitQL/Abstractions/IRelationshipResolver.cs ../PermitQL/Abstractions/IIndexResolver.cs ../PermitQL/Abstractions/IStatisticsResolver.cs ../PermitQL/Abstractions/IProviderCapabilityResolver.cs ../PermitQL/Abstractions/IPermitQLFactory.cs ../PermitQL/Data/AdoNetDataAccessor.cs ../PermitQL/Data/NullConstraintResolver.cs ../PermitQL/Data/NullRelationshipResolver.cs ../PermitQL/Data/NullIndexResolver.cs ../PermitQL/Data/NullStatisticsResolver.cs ../PermitQL/Data/NullProviderCapabilityResolver.cs ../PermitQL/PermitQLFactory.cs ../PermitQL.Tests/Data/AdoNetDataAccessorMetadataTests.cs ../PermitQL.Tests/PermitQLFactoryTests.cs
git commit -m "Add schema metadata resolver infrastructure"
```

### Task 3: Implement PostgreSQL and SQLite metadata resolvers

**Files:**
- Create: `Implementations/MetadataResolvers/PostgresConstraintResolver.cs`
- Create: `Implementations/MetadataResolvers/PostgresRelationshipResolver.cs`
- Create: `Implementations/MetadataResolvers/PostgresIndexResolver.cs`
- Create: `Implementations/MetadataResolvers/PostgresStatisticsResolver.cs`
- Create: `Implementations/MetadataResolvers/SqliteConstraintResolver.cs`
- Create: `Implementations/MetadataResolvers/SqliteRelationshipResolver.cs`
- Create: `Implementations/MetadataResolvers/SqliteIndexResolver.cs`
- Create: `Implementations/MetadataResolvers/SqliteStatisticsResolver.cs`
- Create: `ProviderMetadataResolverFactory.cs`
- Test: `../PermitQL.Tests/Data/PostgresMetadataResolverTests.cs`
- Test: `../PermitQL.Tests/Data/SqliteMetadataResolverTests.cs`

- [ ] **Step 1: Write failing provider resolver tests**

Create `../PermitQL.Tests/Data/SqliteMetadataResolverTests.cs` with an in-memory SQLite test for defaults and inbound relationships:

```csharp
namespace PermitQL.Tests.Data;

using PermitQL.Server.Implementations.MetadataResolvers;
using Microsoft.Data.Sqlite;

public sealed class SqliteMetadataResolverTests
{
    [Fact]
    public async Task ConstraintResolver_ReadsUniqueConstraintsAndDefaults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE parent (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteConstraintResolver();
        var constraints = await resolver.ResolveAsync(connection, "main", "parent");

        Assert.Contains(constraints.Unique, item => item.Columns.SequenceEqual(["code"]));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ../PermitQL.Tests --filter "SqliteMetadataResolverTests|PostgresMetadataResolverTests" -v minimal`
Expected: FAIL with missing resolver classes.

- [ ] **Step 3: Implement SQLite metadata resolvers**

Create `Implementations/MetadataResolvers/SqliteConstraintResolver.cs` using `PRAGMA` metadata and `sqlite_master`:

```csharp
namespace PermitQL.Server.Implementations.MetadataResolvers;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Models;

public sealed class SqliteConstraintResolver : IConstraintResolver
{
    public async ValueTask<TableConstraintMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $@"PRAGMA ""{schema}"".table_info(""{table}"");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var createSql = await ReadCreateTableSqlAsync(connection, table, cancellationToken);
        var unique = await ReadUniqueConstraintsAsync(connection, schema, table, cancellationToken);
        var checks = Regex.Matches(createSql, @"CHECK\s*\((?<expr>[^)]+)\)", RegexOptions.IgnoreCase)
            .Select((match, index) => new CheckConstraintMetadata($"{table}_check_{index + 1}", match.Groups["expr"].Value.Trim()))
            .ToArray();

        return new TableConstraintMetadata(unique, checks);
    }
}
```

Create `Implementations/MetadataResolvers/SqliteRelationshipResolver.cs`:

```csharp
public sealed class SqliteRelationshipResolver : IRelationshipResolver
{
    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveOutboundAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken = default)
        => await ReadForeignKeysAsync(connection, schema, table, cancellationToken);

    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveInboundAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken = default)
    {
        var inbound = new List<ForeignKeyMetadata>();
        foreach (var tableName in await ReadTableNamesAsync(connection, schema, cancellationToken))
        {
            var outbound = await ReadForeignKeysAsync(connection, schema, tableName, cancellationToken);
            inbound.AddRange(outbound.Where(fk => fk.ToSchema == schema && fk.ToTable == table));
        }

        return inbound;
    }
}
```

Create `Implementations/MetadataResolvers/SqliteIndexResolver.cs` using `PRAGMA index_list` and `PRAGMA index_info`, and create `Implementations/MetadataResolvers/SqliteStatisticsResolver.cs` with:

```csharp
public sealed class SqliteStatisticsResolver : IStatisticsResolver
{
    public ValueTask<TableStatisticsMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new TableStatisticsMetadata(null, null));
}
```

- [ ] **Step 4: Implement PostgreSQL metadata resolvers**

Create `Implementations/MetadataResolvers/PostgresRelationshipResolver.cs` using catalog queries:

```csharp
namespace PermitQL.Server.Implementations.MetadataResolvers;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Models;

public sealed class PostgresRelationshipResolver : IRelationshipResolver
{
    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveOutboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                tc.constraint_name,
                kcu.table_schema,
                kcu.table_name,
                kcu.column_name,
                ccu.table_schema,
                ccu.table_name,
                ccu.column_name,
                rc.delete_rule,
                rc.update_rule
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.constraint_schema = tc.table_schema
            JOIN information_schema.referential_constraints rc
              ON rc.constraint_name = tc.constraint_name
             AND rc.constraint_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = @schema
              AND tc.table_name = @table
            ORDER BY kcu.ordinal_position;
            """;
        var schemaParameter = command.CreateParameter();
        schemaParameter.ParameterName = "@schema";
        schemaParameter.Value = schema;
        command.Parameters.Add(schemaParameter);

        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@table";
        tableParameter.Value = table;
        command.Parameters.Add(tableParameter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ForeignKeyMetadata>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ForeignKeyMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8)));
        }

        return rows;
    }
}
```

Create the other PostgreSQL resolvers with these exact sources:

- `PostgresConstraintResolver.cs`
  Read unique and check constraints from `pg_constraint`, grouping `contype = 'u'` into `UniqueConstraintMetadata` and `contype = 'c'` into `CheckConstraintMetadata`.
- `PostgresIndexResolver.cs`
  Read index name, uniqueness, and ordered column names from `pg_index`, `pg_class`, and `pg_attribute`.
- `PostgresStatisticsResolver.cs`
  Read `pg_class.reltuples` joined through `pg_namespace` and return `new TableStatisticsMetadata((long?)Math.Round(reltuples), null)`.
- `AdoNetDataAccessor.GetTableColumnsAsync(...)`
  Replace the current schema-only projection with a provider-aware column query so PostgreSQL columns include `column_default`, `is_generated`, and PK membership from `information_schema.columns` plus `table_constraints` / `key_column_usage`.

- [ ] **Step 5: Add a resolver factory for startup wiring**

Create `ProviderMetadataResolverFactory.cs`:

```csharp
namespace PermitQL.Server;

using PermitQL.Abstractions;
using PermitQL.Data;
using PermitQL.Server.Implementations.MetadataResolvers;

public static class ProviderMetadataResolverFactory
{
    public static (IConstraintResolver Constraints, IRelationshipResolver Relationships, IIndexResolver Indexes, IStatisticsResolver Statistics, IProviderCapabilityResolver Capabilities) Create(SqlDialect dialect)
        => dialect switch
        {
            SqlDialect.PostgreSql => (
                new PostgresConstraintResolver(),
                new PostgresRelationshipResolver(),
                new PostgresIndexResolver(),
                new PostgresStatisticsResolver(),
                new NullProviderCapabilityResolver()),
            SqlDialect.Sqlite => (
                new SqliteConstraintResolver(),
                new SqliteRelationshipResolver(),
                new SqliteIndexResolver(),
                new SqliteStatisticsResolver(),
                new NullProviderCapabilityResolver()),
            _ => (
                NullConstraintResolver.Instance,
                NullRelationshipResolver.Instance,
                NullIndexResolver.Instance,
                NullStatisticsResolver.Instance,
                NullProviderCapabilityResolver.Instance),
        };
}
```

- [ ] **Step 6: Run provider resolver tests**

Run: `dotnet test ../PermitQL.Tests --filter "SqliteMetadataResolverTests|PostgresMetadataResolverTests" -v minimal`
Expected: PASS with SQLite integration tests green and PostgreSQL resolver tests green as catalog-query unit tests that assert emitted SQL and mapped records without requiring a live PostgreSQL server.

- [ ] **Step 7: Commit**

```bash
git add Implementations/MetadataResolvers/SqliteConstraintResolver.cs Implementations/MetadataResolvers/SqliteRelationshipResolver.cs Implementations/MetadataResolvers/SqliteIndexResolver.cs Implementations/MetadataResolvers/SqliteStatisticsResolver.cs Implementations/MetadataResolvers/PostgresConstraintResolver.cs Implementations/MetadataResolvers/PostgresRelationshipResolver.cs Implementations/MetadataResolvers/PostgresIndexResolver.cs Implementations/MetadataResolvers/PostgresStatisticsResolver.cs ProviderMetadataResolverFactory.cs ../PermitQL.Tests/Data/SqliteMetadataResolverTests.cs ../PermitQL.Tests/Data/PostgresMetadataResolverTests.cs
git commit -m "Implement provider-backed schema metadata resolvers"
```

### Task 4: Add validator capability disclosure and startup wiring

**Files:**
- Create: `ValidatorCapabilityDescriptor.cs`
- Modify: `Program.cs`
- Modify: `../PermitQL/DependencyInjection.cs`
- Test: `../PermitQL.Tests/Server/ValidatorCapabilityDescriptorTests.cs`
- Modify: `../PermitQL.Tests/Server/StartupBootstrapTests.cs`

- [ ] **Step 1: Write failing capability descriptor tests**

Create `../PermitQL.Tests/Server/ValidatorCapabilityDescriptorTests.cs`:

```csharp
namespace PermitQL.Tests.Server;

using PermitQL.Models;
using PermitQL.Server;

public sealed class ValidatorCapabilityDescriptorTests
{
    [Fact]
    public void Describe_ReturnsExplicitCapabilityStates()
    {
        var descriptor = new ValidatorCapabilityDescriptor();

        var capabilities = descriptor.Describe();

        Assert.Equal(CapabilitySupport.Unsupported, capabilities.Ctes);
        Assert.NotNull(capabilities.Notes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ../PermitQL.Tests --filter "ValidatorCapabilityDescriptorTests|StartupBootstrapTests" -v minimal`
Expected: FAIL with missing `ValidatorCapabilityDescriptor`.

- [ ] **Step 3: Implement the validator capability descriptor**

Create `ValidatorCapabilityDescriptor.cs`:

```csharp
namespace PermitQL.Server;

using PermitQL.Models;

public sealed class ValidatorCapabilityDescriptor
{
    public QueryCapabilityMetadata Describe()
    {
        return new QueryCapabilityMetadata(
            Ctes: CapabilitySupport.Unsupported,
            Subqueries: CapabilitySupport.Supported,
            DerivedTables: CapabilitySupport.Supported,
            WindowFunctions: CapabilitySupport.Unknown,
            Mutations: CapabilitySupport.Supported,
            Notes:
            [
                "Capability states reflect gateway validation behavior, not only raw SQL dialect support.",
            ]);
    }
}
```

- [ ] **Step 4: Wire metadata resolvers and capability descriptor into startup**

Update the beginning of `Program.cs`:

```csharp
var metadataResolvers = ProviderMetadataResolverFactory.Create(options.Provider);
var validatorCapabilities = new ValidatorCapabilityDescriptor();
```

Update the service registration calls in `Program.cs` and `../PermitQL/DependencyInjection.cs`:

```csharp
builder.Services.AddSingleton(validatorCapabilities);
builder.Services.AddPermitQL(
    rulesDirectory,
    connectionFactory,
    options.Provider,
    metadataResolvers.Constraints,
    metadataResolvers.Relationships,
    metadataResolvers.Indexes,
    metadataResolvers.Statistics,
    metadataResolvers.Capabilities);
```

Update every `AddPermitQL(...)` overload so the final overload becomes:

```csharp
public static IServiceCollection AddPermitQL(
    this IServiceCollection services,
    string rulesDirectory,
    Func<DbConnection> connectionFactory,
    IPermitQLFactory factory,
    IConstraintResolver? constraintResolver = null,
    IRelationshipResolver? relationshipResolver = null,
    IIndexResolver? indexResolver = null,
    IStatisticsResolver? statisticsResolver = null,
    IProviderCapabilityResolver? capabilityResolver = null)
{
    var astProvider = factory.CreateSqlAstProvider();
    var rulesProvider = factory.CreateRulesProvider(rulesDirectory);
    var dataAccessor = factory.CreateDataAccessor(
        connectionFactory,
        constraintResolver,
        relationshipResolver,
        indexResolver,
        statisticsResolver,
        capabilityResolver);
    var validator = factory.CreateQueryValidator();
    var rewriter = factory.CreateQueryRewriter(dataAccessor);

    services.AddSingleton(factory);
    services.AddSingleton(astProvider);
    services.AddSingleton(rulesProvider);
    services.AddSingleton(dataAccessor);
    services.AddSingleton(validator);
    services.AddSingleton(rewriter);
    services.AddSingleton(factory.CreatePipeline(rulesProvider, astProvider, validator, rewriter, dataAccessor));

    return services;
}
```

- [ ] **Step 5: Add a startup smoke test for registration**

Add to `../PermitQL.Tests/Server/StartupBootstrapTests.cs`:

```csharp
[Fact]
public void ProviderMetadataResolverFactory_ForSqlite_ReturnsConcreteResolvers()
{
    var resolvers = ProviderMetadataResolverFactory.Create(SqlDialect.Sqlite);

    Assert.NotNull(resolvers.Constraints);
    Assert.NotNull(resolvers.Relationships);
    Assert.NotNull(resolvers.Indexes);
    Assert.NotNull(resolvers.Statistics);
}
```

- [ ] **Step 6: Run the server wiring tests**

Run: `dotnet test ../PermitQL.Tests --filter "ValidatorCapabilityDescriptorTests|StartupBootstrapTests" -v minimal`
Expected: PASS with capability and resolver wiring tests green.

- [ ] **Step 7: Commit**

```bash
git add ValidatorCapabilityDescriptor.cs Program.cs ../PermitQL/DependencyInjection.cs ../PermitQL.Tests/Server/ValidatorCapabilityDescriptorTests.cs ../PermitQL.Tests/Server/StartupBootstrapTests.cs
git commit -m "Wire describe_database capability metadata into startup"
```

### Task 5: Replace markdown `describe_database` output with governed JSON

**Files:**
- Modify: `Tools/PermitQLTools.cs`
- Test: `../PermitQL.Tests/Server/DescribeDatabaseToolTests.cs`

- [ ] **Step 1: Write failing tool-shaping tests**

Create `../PermitQL.Tests/Server/DescribeDatabaseToolTests.cs`:

```csharp
namespace PermitQL.Tests.Server;

using PermitQL.Abstractions;
using PermitQL.Models;
using PermitQL.Server.Tools;
using NSubstitute;

public sealed class DescribeDatabaseToolTests
{
    [Fact]
    public async Task DescribeDatabase_ReturnsJsonWithFilteredMetadata()
    {
        var dataAccessor = Substitute.For<IDataAccessor>();
        var rulesProvider = Substitute.For<IRulesProvider>();
        var factory = Substitute.For<IPermitQLFactory>();

        rulesProvider.GetRuleSet("main").Returns(new RuleSet
        {
            Database = "main",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 5000, AllowedOperations = ["select"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new()
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["orders"] = new()
                        {
                            AllowedColumns = ["id", "customer_id"],
                            AllowedOperations = ["select", "insert"],
                        },
                    },
                },
            },
        });

        dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns([
                new SchemaColumnMetadata("id", "integer", false, true, null, true, GenerationKind.Identity),
                new SchemaColumnMetadata("customer_id", "integer", false, false, null, false, GenerationKind.None),
                new SchemaColumnMetadata("secret_note", "text", true, false, null, false, GenerationKind.None),
            ]);
        dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata(
                [new UniqueConstraintMetadata("orders_customer_unique", ["customer_id"])],
                []));

        var json = await PermitQLTools.DescribeDatabase(dataAccessor, rulesProvider, factory, new ValidatorCapabilityDescriptor(), "main");

        Assert.Contains("\"dialect\"", json, StringComparison.Ordinal);
        Assert.Contains("\"customer_id\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_note", json, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ../PermitQL.Tests --filter DescribeDatabaseToolTests -v minimal`
Expected: FAIL because the tool still returns markdown and lacks the new dependency shape.

- [ ] **Step 3: Refactor `DescribeDatabase` to build a JSON response DTO**

Update the signature and response assembly in `Tools/PermitQLTools.cs`:

```csharp
public static async Task<string> DescribeDatabase(
    IDataAccessor dataAccessor,
    IRulesProvider rulesProvider,
    IPermitQLFactory factory,
    ValidatorCapabilityDescriptor validatorCapabilities,
    string ruleSetKey,
    CancellationToken cancellationToken = default)
{
    var rules = rulesProvider.GetRuleSet(ruleSetKey);
    var providerCapabilities = await dataAccessor.GetQueryCapabilitiesAsync(cancellationToken);
    var mergedCapabilities = MergeCapabilities(providerCapabilities, validatorCapabilities.Describe());

    var response = new
    {
        database = new
        {
            ruleSetKey,
            dialect = FormatDialect(factory.Dialect),
        },
        limits = new
        {
            maxRowsReturned = rules.GlobalLimits.MaxRowsReturned,
            timeoutMs = rules.GlobalLimits.TimeoutMs,
        },
        capabilities = mergedCapabilities,
        schemas = await BuildSchemaDescriptionsAsync(dataAccessor, rules, cancellationToken),
    };

    return JsonSerializer.Serialize(response, JsonOptions);
}
```

- [ ] **Step 4: Implement strict filtering and omission tracking**

Add focused helpers in `Tools/PermitQLTools.cs` for:

```csharp
private static bool IsVisibleRelationship(ForeignKeyMetadata relationship, RuleSet rules, TableRule sourceRule)
private static IReadOnlyList<object> FilterIndexes(IReadOnlyList<TableIndexMetadata> indexes, TableRule tableRule, List<string> omissions)
private static object BuildConstraints(TableConstraintMetadata constraints, TableRule tableRule, List<string> omissions)
```

Use the helpers to enforce:

- hidden columns excluded from `columns`
- constraints and indexes dropped when any referenced column is hidden
- inbound/outbound relationships filtered against exposed tables and allowed columns
- `omissions` populated with machine-readable markers such as `hidden_constraints_omitted`

- [ ] **Step 5: Update the HTTP endpoint contract**

Change the endpoint in `Program.cs` to return raw description JSON instead of `{ description: "..." }`:

```csharp
app.MapGet("/api/databases/{key}", async (string key, IDataAccessor dataAccessor, IRulesProvider rulesProvider, IPermitQLFactory factory, ValidatorCapabilityDescriptor validatorCapabilities, CancellationToken ct) =>
{
    try
    {
        var description = await PermitQL.Server.Tools.PermitQLTools.DescribeDatabase(dataAccessor, rulesProvider, factory, validatorCapabilities, key, ct);
        return Results.Content(description, "application/json");
    }
    catch (Exception ex)
    {
        var (message, type, statusCode) = ErrorHandler.Classify(ex);
        return Results.Json(new ErrorResponse(message, type), statusCode: statusCode);
    }
});
```

- [ ] **Step 6: Run the tool test to verify it passes**

Run: `dotnet test ../PermitQL.Tests --filter DescribeDatabaseToolTests -v minimal`
Expected: PASS with JSON-shaping and filtering assertions green.

- [ ] **Step 7: Commit**

```bash
git add Tools/PermitQLTools.cs Program.cs ../PermitQL.Tests/Server/DescribeDatabaseToolTests.cs
git commit -m "Emit governed JSON from describe_database"
```

### Task 6: Run broader verification and close remaining gaps

**Files:**
- Modify: `../PermitQL.Tests/Server/DescribeDatabaseToolTests.cs`
- Modify: `../PermitQL.Tests/Data/AdoNetDataAccessorMetadataTests.cs`
- Modify: `../PermitQL.Tests/Data/PostgresMetadataResolverTests.cs`
- Modify: `../PermitQL.Tests/Data/SqliteMetadataResolverTests.cs`

- [ ] **Step 1: Add absence-semantics assertions**

Extend `../PermitQL.Tests/Server/DescribeDatabaseToolTests.cs` with a case that distinguishes empty arrays, `null`, and omission markers:

```csharp
[Fact]
public async Task DescribeDatabase_UsesConsistentAbsenceSemantics()
{
    var dataAccessor = Substitute.For<IDataAccessor>();
    var rulesProvider = Substitute.For<IRulesProvider>();
    var factory = Substitute.For<IPermitQLFactory>();
    factory.Dialect.Returns(SqlDialect.Sqlite);

    rulesProvider.GetRuleSet("main").Returns(new RuleSet
    {
        Database = "main",
        GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 5000, AllowedOperations = ["select"] },
        ExposedSchemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new()
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["events"] = new() { AllowedColumns = ["id"], AllowedOperations = ["select"] },
                },
            },
        },
    });

    dataAccessor.GetTableColumnsAsync("public", "events", Arg.Any<CancellationToken>())
        .Returns([new SchemaColumnMetadata("id", "integer", false, true, null, false, GenerationKind.None)]);
    dataAccessor.GetTableConstraintsAsync("public", "events", Arg.Any<CancellationToken>())
        .Returns(new TableConstraintMetadata([], []));
    dataAccessor.GetOutboundForeignKeysAsync("public", "events", Arg.Any<CancellationToken>()).Returns([]);
    dataAccessor.GetInboundForeignKeysAsync("public", "events", Arg.Any<CancellationToken>()).Returns([]);
    dataAccessor.GetTableIndexesAsync("public", "events", Arg.Any<CancellationToken>()).Returns([]);
    dataAccessor.GetTableStatisticsAsync("public", "events", Arg.Any<CancellationToken>())
        .Returns(new TableStatisticsMetadata(null, null));
    dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
        .Returns(new QueryCapabilityMetadata(CapabilitySupport.Unknown, CapabilitySupport.Unknown, CapabilitySupport.Unknown, CapabilitySupport.Unknown, CapabilitySupport.Unknown, []));

    var json = await PermitQLTools.DescribeDatabase(dataAccessor, rulesProvider, factory, new ValidatorCapabilityDescriptor(), "main");

    Assert.Contains("\"indexes\":[]", json, StringComparison.Ordinal);
    Assert.Contains("\"approximateRowCount\":null", json, StringComparison.Ordinal);
    Assert.Contains("\"omissions\":[\"unavailable_statistics\"]", json, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Add a lightweight statistics behavior test**

Extend `../PermitQL.Tests/Data/SqliteMetadataResolverTests.cs`:

```csharp
[Fact]
public async Task StatisticsResolver_ReturnsUnknownInsteadOfScanning()
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();

    var resolver = new SqliteStatisticsResolver();

    var stats = await resolver.ResolveAsync(connection, "main", "parent");

    Assert.Null(stats.ApproximateRowCount);
}
```

- [ ] **Step 3: Run the focused metadata suite**

Run: `dotnet test ../PermitQL.Tests --filter "DescribeDatabaseToolTests|AdoNetDataAccessorMetadataTests|SqliteMetadataResolverTests|PostgresMetadataResolverTests|ValidatorCapabilityDescriptorTests|StartupBootstrapTests|PermitQLFactoryTests" -v minimal`
Expected: PASS with all describe-database-related tests green.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test ../PermitQL.Tests -v minimal`
Expected: PASS with all existing tests still green and no regressions outside the describe-database area.

- [ ] **Step 5: Commit**

```bash
git add ../PermitQL.Tests/Server/DescribeDatabaseToolTests.cs ../PermitQL.Tests/Data/AdoNetDataAccessorMetadataTests.cs ../PermitQL.Tests/Data/PostgresMetadataResolverTests.cs ../PermitQL.Tests/Data/SqliteMetadataResolverTests.cs
git commit -m "Verify describe_database metadata behavior end to end"
```
