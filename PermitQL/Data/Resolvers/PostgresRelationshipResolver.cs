namespace PermitQL.Data.Resolvers;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class PostgresRelationshipResolver : IRelationshipResolver
{
    private const string OutboundQuery = """
        SELECT
            kcu.constraint_name,
            kcu.column_name,
            ccu.table_schema AS referenced_schema,
            ccu.table_name AS referenced_table,
            ccu.column_name AS referenced_column,
            rc.delete_rule,
            rc.update_rule
        FROM information_schema.key_column_usage kcu
        JOIN information_schema.referential_constraints rc
            ON kcu.constraint_name = rc.constraint_name
            AND kcu.constraint_schema = rc.constraint_schema
        JOIN information_schema.constraint_column_usage ccu
            ON rc.unique_constraint_name = ccu.constraint_name
            AND rc.unique_constraint_schema = ccu.constraint_schema
        WHERE kcu.table_schema = @schema AND kcu.table_name = @table
        ORDER BY kcu.constraint_name, kcu.ordinal_position
        """;

    private const string InboundQuery = """
        SELECT
            kcu.constraint_name,
            kcu.table_schema AS from_schema,
            kcu.table_name AS from_table,
            kcu.column_name AS from_column,
            ccu.column_name AS to_column,
            rc.delete_rule,
            rc.update_rule
        FROM information_schema.key_column_usage kcu
        JOIN information_schema.referential_constraints rc
            ON kcu.constraint_name = rc.constraint_name
            AND kcu.constraint_schema = rc.constraint_schema
        JOIN information_schema.constraint_column_usage ccu
            ON rc.unique_constraint_name = ccu.constraint_name
            AND rc.unique_constraint_schema = ccu.constraint_schema
        WHERE ccu.table_schema = @schema AND ccu.table_name = @table
        ORDER BY kcu.constraint_name, kcu.ordinal_position
        """;

    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveOutboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = OutboundQuery;

        AddSchemaAndTableParams(command, schema, table);

        var results = new List<ForeignKeyMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ForeignKeyMetadata(
                ConstraintName: reader.GetString(0),
                FromSchema: schema,
                FromTable: table,
                FromColumn: reader.GetString(1),
                ToSchema: reader.GetString(2),
                ToTable: reader.GetString(3),
                ToColumn: reader.GetString(4),
                OnDelete: NormalizeAction(reader.GetString(5)),
                OnUpdate: NormalizeAction(reader.GetString(6))));
        }

        return results;
    }

    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveInboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = InboundQuery;

        AddSchemaAndTableParams(command, schema, table);

        var results = new List<ForeignKeyMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ForeignKeyMetadata(
                ConstraintName: reader.GetString(0),
                FromSchema: reader.GetString(1),
                FromTable: reader.GetString(2),
                FromColumn: reader.GetString(3),
                ToSchema: schema,
                ToTable: table,
                ToColumn: reader.GetString(4),
                OnDelete: NormalizeAction(reader.GetString(5)),
                OnUpdate: NormalizeAction(reader.GetString(6))));
        }

        return results;
    }

    private static void AddSchemaAndTableParams(DbCommand command, string schema, string table)
    {
        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@schema";
        schemaParam.Value = schema;
        command.Parameters.Add(schemaParam);

        var tableParam = command.CreateParameter();
        tableParam.ParameterName = "@table";
        tableParam.Value = table;
        command.Parameters.Add(tableParam);
    }

    private static string? NormalizeAction(string action)
        => string.Equals(action, "NO ACTION", StringComparison.OrdinalIgnoreCase) ? null : action;
}
