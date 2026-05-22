namespace PermitQL.Server.Implementations.MetadataResolvers;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Models;

public sealed class PostgresConstraintResolver : IConstraintResolver
{
    private const string Query = """
        SELECT
            CASE c.contype WHEN 'u' THEN 'u' WHEN 'c' THEN 'c' END AS constraint_type,
            c.conname AS constraint_name,
            CASE c.contype
                WHEN 'u' THEN (
                    SELECT string_agg(a.attname, ',' ORDER BY a.attnum)
                    FROM unnest(c.conkey) AS k(col)
                    JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.col
                )
                WHEN 'c' THEN pg_get_constraintdef(c.oid)
            END AS detail
        FROM pg_constraint c
        JOIN pg_namespace n ON n.oid = c.connamespace
        WHERE n.nspname = @schema
          AND c.conrelid = (
              SELECT oid FROM pg_class
              WHERE relname = @table AND relnamespace = n.oid
          )
          AND c.contype IN ('u', 'c')
        ORDER BY c.contype, c.conname
        """;

    public async ValueTask<TableConstraintMetadata> ResolveAsync(
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

        var unique = new List<UniqueConstraintMetadata>();
        var check = new List<CheckConstraintMetadata>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(0);
            var name = reader.GetString(1);
            var detail = reader.GetString(2);

            switch (type)
            {
                case "u":
                    var columns = detail.Split(',', StringSplitOptions.TrimEntries);
                    unique.Add(new UniqueConstraintMetadata(name, columns));
                    break;
                case "c":
                    check.Add(new CheckConstraintMetadata(name, detail));
                    break;
            }
        }

        return new TableConstraintMetadata(unique, check);
    }
}
