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
        var (connection, command, reader) = CreateMockChain();

        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(false));

        var resolver = new PostgresStatisticsResolver();
        await resolver.ResolveAsync(connection, "public", "orders");

        // Verify query references pg_class and pg_namespace
        Assert.Contains("pg_class", command.CommandText);
        Assert.Contains("pg_namespace", command.CommandText);
    }

    [Fact]
    public async Task StatisticsResolver_MapsRowCount()
    {
        var (connection, command, reader) = CreateMockChain();

        var callCount = 0;
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(callCount++ < 1));

        reader.GetFloat(0).Returns(42000f);

        var resolver = new PostgresStatisticsResolver();
        var stats = await resolver.ResolveAsync(connection, "public", "orders");

        Assert.Equal(42000, stats.ApproximateRowCount);
    }
}
