namespace PermitQL.Server.Implementations.MetadataResolvers;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Models;

public sealed class PostgresStatisticsResolver : IStatisticsResolver
{
    private const string Query = """
        SELECT c.reltuples
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = @schema AND c.relname = @table
        """;

    public async ValueTask<TableStatisticsMetadata> ResolveAsync(
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

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var reltuples = reader.GetFloat(0);
            var rowCount = reltuples < 0 ? null : (long?)Convert.ToInt64(reltuples);
            return new TableStatisticsMetadata(rowCount, null);
        }

        return new TableStatisticsMetadata(null, null);
    }
}
