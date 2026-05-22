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

    [Fact]
    public async Task ConstraintResolver_ReadsCheckConstraints()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                price REAL NOT NULL CHECK(price > 0),
                quantity INTEGER CHECK(quantity >= 0)
            );
            """;
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteConstraintResolver();
        var constraints = await resolver.ResolveAsync(connection, "main", "products");

        Assert.NotEmpty(constraints.Check);
    }

    [Fact]
    public async Task ConstraintResolver_ReadsNestedCheckConstraints()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE nested_checks (
                id INTEGER PRIMARY KEY,
                price REAL CHECK((price > 0) AND (price < 10000)),
                qty INTEGER CHECK(qty >= 0)
            );
            """;
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteConstraintResolver();
        var constraints = await resolver.ResolveAsync(connection, "main", "nested_checks");

        Assert.Equal(2, constraints.Check.Count);
        Assert.Contains(constraints.Check, c => c.Expression.Contains("price > 0"));
        Assert.Contains(constraints.Check, c => c.Expression.Contains("qty >= 0"));
    }

    [Fact]
    public async Task RelationshipResolver_ResolveOutbound_ReadsForeignKeys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE parent (id INTEGER PRIMARY KEY);
            CREATE TABLE child (
                id INTEGER PRIMARY KEY,
                parent_id INTEGER NOT NULL REFERENCES parent(id) ON DELETE CASCADE ON UPDATE SET NULL
            );
            """;
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteRelationshipResolver();
        var fks = await resolver.ResolveOutboundAsync(connection, "main", "child");

        Assert.Single(fks);
        var fk = fks[0];
        Assert.Equal("parent", fk.ToTable);
        Assert.Equal("parent_id", fk.FromColumn);
        Assert.Equal("id", fk.ToColumn);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Equal("SET NULL", fk.OnUpdate);
    }

    [Fact]
    public async Task RelationshipResolver_ResolveInbound_FindsReferencingTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE parent (id INTEGER PRIMARY KEY);
            CREATE TABLE child1 (
                id INTEGER PRIMARY KEY,
                parent_id INTEGER REFERENCES parent(id)
            );
            CREATE TABLE child2 (
                id INTEGER PRIMARY KEY,
                parent_id INTEGER REFERENCES parent(id)
            );
            CREATE TABLE unrelated (id INTEGER PRIMARY KEY);
            """;
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteRelationshipResolver();
        var inbound = await resolver.ResolveInboundAsync(connection, "main", "parent");

        Assert.Equal(2, inbound.Count);
        Assert.Contains(inbound, fk => fk.FromTable == "child1");
        Assert.Contains(inbound, fk => fk.FromTable == "child2");
    }

    [Fact]
    public async Task IndexResolver_ReadsIndexes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE items (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                code TEXT NOT NULL
            );
            CREATE INDEX idx_items_name ON items(name);
            CREATE UNIQUE INDEX idx_items_code ON items(code);
            """;
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteIndexResolver();
        var indexes = await resolver.ResolveAsync(connection, "main", "items");

        Assert.Contains(indexes, idx => idx.Name == "idx_items_name" && !idx.IsUnique && idx.Columns.SequenceEqual(["name"]));
        Assert.Contains(indexes, idx => idx.Name == "idx_items_code" && idx.IsUnique && idx.Columns.SequenceEqual(["code"]));
    }

    [Fact]
    public async Task IndexResolver_ReadsCompositeIndexes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE events (
                id INTEGER PRIMARY KEY,
                year INTEGER NOT NULL,
                month INTEGER NOT NULL
            );
            CREATE INDEX idx_events_year_month ON events(year, month);
            """;
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteIndexResolver();
        var indexes = await resolver.ResolveAsync(connection, "main", "events");

        Assert.Contains(indexes, idx => idx.Name == "idx_events_year_month" && idx.Columns.SequenceEqual(["year", "month"]));
    }

    [Fact]
    public async Task StatisticsResolver_ReturnsNullStatistics()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE empty_table (id INTEGER PRIMARY KEY);";
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "main", "empty_table");

        Assert.Null(stats.ApproximateRowCount);
        Assert.Null(stats.LastAnalyzed);
    }

    [Fact]
    public async Task StatisticsResolver_ReturnsUnknownInsteadOfScanning()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE parent (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL UNIQUE
            );
            INSERT INTO parent (code) VALUES ('A'), ('B'), ('C');
            """;
        await command.ExecuteNonQueryAsync();

        var resolver = new SqliteStatisticsResolver();

        var stats = await resolver.ResolveAsync(connection, "main", "parent");

        // Even though rows exist, SQLite resolver returns null (does not COUNT(*) scan)
        Assert.Null(stats.ApproximateRowCount);
    }
}
