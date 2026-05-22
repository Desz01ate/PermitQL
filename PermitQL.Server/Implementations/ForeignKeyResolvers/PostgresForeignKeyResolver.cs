namespace PermitQL.Server.Implementations.ForeignKeyResolvers;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Models;

public sealed class PostgresForeignKeyResolver : IForeignKeyResolver
{
    private const string Query = """
        SELECT
            kcu.constraint_name,
            kcu.column_name,
            ccu.table_schema AS referenced_schema,
            ccu.table_name AS referenced_table,
            ccu.column_name AS referenced_column
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

    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Query;

        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@schema";
        schemaParam.Value = schema;
        command.Parameters.Add(schemaParam);

        var tableParam = command.CreateParameter();
        tableParam.ParameterName = "@table";
        tableParam.Value = table;
        command.Parameters.Add(tableParam);

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
                OnDelete: null,
                OnUpdate: null));
        }

        return results;
    }
}
