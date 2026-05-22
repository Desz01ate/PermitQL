namespace PermitQL.Tests.Data;

using System.Data;
using System.Data.Common;
using PermitQL.Data.Resolvers;
using NSubstitute;

public sealed class PostgresMetadataResolverTests
{
    private static (DbConnection connection, DbCommand command, DbDataReader reader) CreateMockChain()
    {
        var connection = Substitute.For<DbConnection>();
        var command = Substitute.For<DbCommand>();
        var reader = Substitute.For<DbDataReader>();
        var parameters = Substitute.For<DbParameterCollection>();
        var parameter = Substitute.For<DbParameter>();

        connection.CreateCommand().Returns(command);
        command.CreateParameter().Returns(parameter);
        command.Parameters.Returns(parameters);
        command.ExecuteReaderAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(reader));

        return (connection, command, reader);
    }

    private static (DbConnection connection, DbCommand cmd1, DbDataReader reader1,
        DbCommand cmd2, DbDataReader reader2) CreateDualCommandMockChain()
    {
        var connection = Substitute.For<DbConnection>();

        var cmd1 = Substitute.For<DbCommand>();
        var reader1 = Substitute.For<DbDataReader>();
        var params1 = Substitute.For<DbParameterCollection>();
        var param1 = Substitute.For<DbParameter>();
        cmd1.CreateParameter().Returns(param1);
        cmd1.Parameters.Returns(params1);
        cmd1.ExecuteReaderAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(reader1));

        var cmd2 = Substitute.For<DbCommand>();
        var reader2 = Substitute.For<DbDataReader>();
        var params2 = Substitute.For<DbParameterCollection>();
        var param2 = Substitute.For<DbParameter>();
        cmd2.CreateParameter().Returns(param2);
        cmd2.Parameters.Returns(params2);
        cmd2.ExecuteReaderAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(reader2));

        connection.CreateCommand().Returns(cmd1, cmd2);

        return (connection, cmd1, reader1, cmd2, reader2);
    }

    [Fact]
    public async Task ConstraintResolver_EmitsCorrectQuery_AndMapsResults()
    {
        var (connection, command, reader) = CreateMockChain();

        var callCount = 0;
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(callCount++ < 2));

        // Row 1: unique constraint
        reader.GetString(0).Returns("u", "c");
        reader.GetString(1).Returns("uq_email", "ck_age");
        // For unique constraint: columns come from ordinal 2
        reader.GetString(2).Returns("email", "age > 0");

        var resolver = new PostgresConstraintResolver();
        var result = await resolver.ResolveAsync(connection, "public", "users");

        // Verify the command text contains the expected catalog query patterns
        Assert.Contains("pg_constraint", command.CommandText);

        // Verify mapped results
        Assert.Single(result.Unique);
        Assert.Equal("uq_email", result.Unique[0].Name);
        Assert.Equal(["email"], result.Unique[0].Columns);

        Assert.Single(result.Check);
        Assert.Equal("ck_age", result.Check[0].Name);
        Assert.Equal("age > 0", result.Check[0].Expression);
    }

    [Fact]
    public async Task RelationshipResolver_Outbound_EmitsParameterizedQuery_AndMapsResults()
    {
        var (connection, command, reader) = CreateMockChain();

        var callCount = 0;
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(callCount++ < 1));

        // Outbound columns: 0=constraint_name, 1=column_name, 2=referenced_schema,
        //   3=referenced_table, 4=referenced_column, 5=delete_rule, 6=update_rule
        reader.GetString(0).Returns("fk_orders_customer");
        reader.GetString(1).Returns("customer_id");
        reader.GetString(2).Returns("public");
        reader.GetString(3).Returns("customers");
        reader.GetString(4).Returns("id");
        reader.GetString(5).Returns("CASCADE");
        reader.GetString(6).Returns("NO ACTION");

        var resolver = new PostgresRelationshipResolver();
        var result = await resolver.ResolveOutboundAsync(connection, "public", "orders");

        // Verify query references information_schema
        Assert.Contains("information_schema", command.CommandText);

        // Verify mapped results
        Assert.Single(result);
        var fk = result[0];
        Assert.Equal("fk_orders_customer", fk.ConstraintName);
        Assert.Equal("orders", fk.FromTable);
        Assert.Equal("customer_id", fk.FromColumn);
        Assert.Equal("customers", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Null(fk.OnUpdate); // "NO ACTION" normalized to null
    }

    [Fact]
    public async Task RelationshipResolver_Inbound_EmitsParameterizedQuery_AndMapsResults()
    {
        var (connection, command, reader) = CreateMockChain();

        var callCount = 0;
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(callCount++ < 1));

        // Inbound columns: 0=constraint_name, 1=from_schema, 2=from_table,
        //   3=from_column, 4=to_column, 5=delete_rule, 6=update_rule
        reader.GetString(0).Returns("fk_line_items_order");
        reader.GetString(1).Returns("public");
        reader.GetString(2).Returns("line_items");
        reader.GetString(3).Returns("order_id");
        reader.GetString(4).Returns("id");
        reader.GetString(5).Returns("CASCADE");
        reader.GetString(6).Returns("NO ACTION");

        var resolver = new PostgresRelationshipResolver();
        var result = await resolver.ResolveInboundAsync(connection, "public", "orders");

        // Verify query references information_schema and targets the referenced table
        Assert.Contains("information_schema", command.CommandText);

        // Verify mapped results
        Assert.Single(result);
        var fk = result[0];
        Assert.Equal("fk_line_items_order", fk.ConstraintName);
        Assert.Equal("line_items", fk.FromTable);
        Assert.Equal("order_id", fk.FromColumn);
        Assert.Equal("orders", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Null(fk.OnUpdate); // "NO ACTION" normalized to null
    }

    [Fact]
    public async Task IndexResolver_EmitsCorrectQuery_AndMapsResults()
    {
        var (connection, command, reader) = CreateMockChain();

        var callCount = 0;
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(callCount++ < 1));

        // Index columns: 0=index_name, 1=is_unique, 2=columns (comma-separated)
        reader.GetString(0).Returns("idx_orders_date");
        reader.GetBoolean(1).Returns(false);
        reader.GetString(2).Returns("order_date,status");

        var resolver = new PostgresIndexResolver();
        var result = await resolver.ResolveAsync(connection, "public", "orders");

        // Verify query references pg_index catalog tables
        Assert.Contains("pg_index", command.CommandText);

        // Verify mapped results
        Assert.Single(result);
        Assert.Equal("idx_orders_date", result[0].Name);
        Assert.False(result[0].IsUnique);
        Assert.Equal(["order_date", "status"], result[0].Columns);
    }

    [Fact]
    public async Task StatisticsResolver_EmitsCorrectQuery()
    {
        var (connection, cmd1, reader1, cmd2, reader2) = CreateDualCommandMockChain();

        reader1.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
        reader2.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var resolver = new PostgresStatisticsResolver();
        await resolver.ResolveAsync(connection, "public", "orders");

        Assert.Contains("pg_stat_all_tables", cmd1.CommandText);
        Assert.Contains("pg_stats", cmd2.CommandText);
    }

    [Fact]
    public async Task StatisticsResolver_MapsRowCount()
    {
        var (connection, cmd1, reader1, cmd2, reader2) = CreateDualCommandMockChain();

        var callCount = 0;
        reader1.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(callCount++ < 1));
        reader1.IsDBNull(0).Returns(false);
        reader1.GetInt64(0).Returns(42000L);
        reader1.IsDBNull(1).Returns(true);

        reader2.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var resolver = new PostgresStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "public", "orders");

        Assert.Equal(42000, stats.ApproximateRowCount);
    }

    // ==================== Column statistics tests ====================

    [Fact]
    public async Task StatisticsResolver_ParsesColumnStatistics()
    {
        var (connection, cmd1, reader1, cmd2, reader2) = CreateDualCommandMockChain();

        // Table-level stats
        var tableReadCount = 0;
        reader1.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(tableReadCount++ < 1));
        reader1.IsDBNull(0).Returns(false);
        reader1.GetInt64(0).Returns(100L);
        reader1.IsDBNull(1).Returns(true);

        // Column-level stats: one row for "status" column
        var colReadCount = 0;
        reader2.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(colReadCount++ < 1));
        reader2.GetString(0).Returns("status");
        reader2.IsDBNull(1).Returns(false);
        reader2.GetFloat(1).Returns(0.05f);
        reader2.IsDBNull(2).Returns(false);
        reader2.GetFloat(2).Returns(3f);
        reader2.IsDBNull(3).Returns(false);
        reader2.GetString(3).Returns("{active,pending,closed}");
        reader2.IsDBNull(4).Returns(false);
        reader2.GetString(4).Returns("{0.6,0.3,0.1}");
        reader2.IsDBNull(5).Returns(true);

        var resolver = new PostgresStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "public", "orders");

        Assert.NotNull(stats.ColumnStatistics);
        Assert.True(stats.ColumnStatistics.ContainsKey("status"));

        var colStats = stats.ColumnStatistics["status"];
        Assert.Equal(0.05, colStats.NullFraction!.Value, precision: 3);
        Assert.Equal(3, colStats.ApproximateDistinctCount);
        Assert.Equal(["active", "pending", "closed"], colStats.MostCommonValues);
        Assert.Equal([0.6, 0.3, 0.1], colStats.MostCommonFrequencies);
        Assert.Null(colStats.MinValue);
        Assert.Null(colStats.MaxValue);
    }

    [Fact]
    public async Task StatisticsResolver_HandlesNegativeNDistinct()
    {
        var (connection, cmd1, reader1, cmd2, reader2) = CreateDualCommandMockChain();

        var tableReadCount = 0;
        reader1.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(tableReadCount++ < 1));
        reader1.IsDBNull(0).Returns(false);
        reader1.GetInt64(0).Returns(200L);
        reader1.IsDBNull(1).Returns(true);

        var colReadCount = 0;
        reader2.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(colReadCount++ < 1));
        reader2.GetString(0).Returns("email");
        reader2.IsDBNull(1).Returns(false);
        reader2.GetFloat(1).Returns(0f);
        reader2.IsDBNull(2).Returns(false);
        reader2.GetFloat(2).Returns(-1f); // every value unique
        reader2.IsDBNull(3).Returns(true);
        reader2.IsDBNull(4).Returns(true);
        reader2.IsDBNull(5).Returns(true);

        var resolver = new PostgresStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "public", "users");

        Assert.NotNull(stats.ColumnStatistics);
        Assert.Equal(200, stats.ColumnStatistics["email"].ApproximateDistinctCount);
    }

    [Fact]
    public async Task StatisticsResolver_CapsCommonValuesAtTen()
    {
        var (connection, cmd1, reader1, cmd2, reader2) = CreateDualCommandMockChain();

        var tableReadCount = 0;
        reader1.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(tableReadCount++ < 1));
        reader1.IsDBNull(0).Returns(false);
        reader1.GetInt64(0).Returns(1000L);
        reader1.IsDBNull(1).Returns(true);

        var vals = string.Join(",", Enumerable.Range(1, 15).Select(i => $"v{i}"));
        var freqs = string.Join(",", Enumerable.Range(1, 15).Select(i => $"0.0{i}"));

        var colReadCount = 0;
        reader2.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(colReadCount++ < 1));
        reader2.GetString(0).Returns("category");
        reader2.IsDBNull(1).Returns(false);
        reader2.GetFloat(1).Returns(0f);
        reader2.IsDBNull(2).Returns(false);
        reader2.GetFloat(2).Returns(15f);
        reader2.IsDBNull(3).Returns(false);
        reader2.GetString(3).Returns($"{{{vals}}}");
        reader2.IsDBNull(4).Returns(false);
        reader2.GetString(4).Returns($"{{{freqs}}}");
        reader2.IsDBNull(5).Returns(true);

        var resolver = new PostgresStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "public", "products");

        Assert.NotNull(stats.ColumnStatistics);
        var colStats = stats.ColumnStatistics["category"];
        Assert.Equal(10, colStats.MostCommonValues!.Count);
        Assert.Equal(10, colStats.MostCommonFrequencies!.Count);
        Assert.Equal("v1", colStats.MostCommonValues[0]);
        Assert.Equal("v10", colStats.MostCommonValues[9]);
    }

    [Fact]
    public async Task StatisticsResolver_ExtractsMinMaxFromHistogramBounds()
    {
        var (connection, cmd1, reader1, cmd2, reader2) = CreateDualCommandMockChain();

        var tableReadCount = 0;
        reader1.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(tableReadCount++ < 1));
        reader1.IsDBNull(0).Returns(false);
        reader1.GetInt64(0).Returns(100L);
        reader1.IsDBNull(1).Returns(true);

        var colReadCount = 0;
        reader2.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(colReadCount++ < 1));
        reader2.GetString(0).Returns("salary");
        reader2.IsDBNull(1).Returns(false);
        reader2.GetFloat(1).Returns(0f);
        reader2.IsDBNull(2).Returns(false);
        reader2.GetFloat(2).Returns(20f);
        reader2.IsDBNull(3).Returns(true);
        reader2.IsDBNull(4).Returns(true);
        reader2.IsDBNull(5).Returns(false);
        reader2.GetString(5).Returns("{2100,4200,8000,12000,24000}");

        var resolver = new PostgresStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "public", "employees");

        Assert.NotNull(stats.ColumnStatistics);
        var colStats = stats.ColumnStatistics["salary"];
        Assert.Equal("2100", colStats.MinValue);
        Assert.Equal("24000", colStats.MaxValue);
    }

    [Fact]
    public async Task StatisticsResolver_NoColumnStatsRows_ReturnsNullColumnStatistics()
    {
        var (connection, cmd1, reader1, cmd2, reader2) = CreateDualCommandMockChain();

        var tableReadCount = 0;
        reader1.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(tableReadCount++ < 1));
        reader1.IsDBNull(0).Returns(false);
        reader1.GetInt64(0).Returns(50L);
        reader1.IsDBNull(1).Returns(true);

        reader2.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var resolver = new PostgresStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "public", "empty_stats");

        Assert.Equal(50, stats.ApproximateRowCount);
        Assert.Null(stats.ColumnStatistics);
    }

    [Fact]
    public async Task StatisticsResolver_OmitsColumnsWithAllNullStats()
    {
        var (connection, cmd1, reader1, cmd2, reader2) = CreateDualCommandMockChain();

        var tableReadCount = 0;
        reader1.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(tableReadCount++ < 1));
        reader1.IsDBNull(0).Returns(false);
        reader1.GetInt64(0).Returns(100L);
        reader1.IsDBNull(1).Returns(true);

        var colReadCount = 0;
        reader2.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(colReadCount++ < 1));
        reader2.GetString(0).Returns("mystery_col");
        reader2.IsDBNull(1).Returns(true);
        reader2.IsDBNull(2).Returns(true);
        reader2.IsDBNull(3).Returns(true);
        reader2.IsDBNull(4).Returns(true);
        reader2.IsDBNull(5).Returns(true);

        var resolver = new PostgresStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "public", "t");

        Assert.Null(stats.ColumnStatistics);
    }

    // ==================== ParsePgTextArray unit tests ====================

    [Fact]
    public void ParsePgTextArray_SimpleValues()
    {
        var result = PostgresStatisticsResolver.ParsePgTextArray("{a,b,c}");
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void ParsePgTextArray_QuotedValuesWithCommas()
    {
        var result = PostgresStatisticsResolver.ParsePgTextArray("{simple,\"has,comma\",end}");
        Assert.Equal(["simple", "has,comma", "end"], result);
    }

    [Fact]
    public void ParsePgTextArray_EscapedQuotes()
    {
        var result = PostgresStatisticsResolver.ParsePgTextArray("{\"val\\\"ue\",other}");
        Assert.NotNull(result);
        Assert.Equal("val\"ue", result[0]);
        Assert.Equal("other", result[1]);
    }

    [Fact]
    public void ParsePgTextArray_NullInput_ReturnsNull()
    {
        Assert.Null(PostgresStatisticsResolver.ParsePgTextArray(null));
        Assert.Null(PostgresStatisticsResolver.ParsePgTextArray(""));
        Assert.Null(PostgresStatisticsResolver.ParsePgTextArray("{}"));
    }

    [Fact]
    public void ParsePgTextArray_SingleElement()
    {
        var result = PostgresStatisticsResolver.ParsePgTextArray("{only}");
        Assert.Equal(["only"], result);
    }
}
