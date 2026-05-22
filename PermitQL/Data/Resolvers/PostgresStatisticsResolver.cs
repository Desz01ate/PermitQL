namespace PermitQL.Data.Resolvers;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class PostgresStatisticsResolver : IStatisticsResolver
{
    private const string Query
        = """
          SELECT
              s.n_live_tup,
              GREATEST(
                  COALESCE(s.last_analyze, '-infinity'),
                  COALESCE(s.last_autoanalyze, '-infinity')
              ) AS last_analyzed_at
          FROM pg_stat_all_tables s
          WHERE s.schemaname = @schema
            AND s.relname = @table
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
            long? rowCount = reader.IsDBNull(0)
                ? null
                : reader.GetInt64(0);

            DateTimeOffset? lastAnalyzed = reader.IsDBNull(1)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(1);

            return new TableStatisticsMetadata(rowCount, lastAnalyzed);
        }

        return new TableStatisticsMetadata(null, null);
    }
}