namespace PermitQL.Data.Resolvers;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class PostgresIndexResolver : IIndexResolver
{
    private const string Query = """
        SELECT
            ic.relname AS index_name,
            i.indisunique AS is_unique,
            array_to_string(
                ARRAY(
                    SELECT a.attname
                    FROM unnest(i.indkey) WITH ORDINALITY AS k(attnum, ord)
                    JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                    ORDER BY k.ord
                ),
                ','
            ) AS columns
        FROM pg_index i
        JOIN pg_class ic ON ic.oid = i.indexrelid
        JOIN pg_class tc ON tc.oid = i.indrelid
        JOIN pg_namespace n ON n.oid = tc.relnamespace
        WHERE n.nspname = @schema
          AND tc.relname = @table
          AND NOT i.indisprimary
        ORDER BY ic.relname
        """;

    public async ValueTask<IReadOnlyList<TableIndexMetadata>> ResolveAsync(
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

        var results = new List<TableIndexMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            var isUnique = reader.GetBoolean(1);
            var columnsStr = reader.GetString(2);
            var columns = columnsStr.Split(',', StringSplitOptions.TrimEntries);

            results.Add(new TableIndexMetadata(name, columns, isUnique));
        }

        return results;
    }
}
