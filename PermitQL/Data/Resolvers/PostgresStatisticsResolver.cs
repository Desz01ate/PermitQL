namespace PermitQL.Data.Resolvers;

using System.Data.Common;
using System.Text;
using Abstractions;
using Models;

public sealed class PostgresStatisticsResolver : IStatisticsResolver
{
    private const int MaxCommonValues = 10;

    private const string TableStatsQuery
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

    private const string ColumnStatsQuery
        = """
          SELECT
              attname,
              null_frac,
              n_distinct,
              most_common_vals::text,
              most_common_freqs::text,
              histogram_bounds::text
          FROM pg_stats
          WHERE schemaname = @schema
            AND tablename = @table
          """;

    public async ValueTask<TableStatisticsMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        var (rowCount, lastAnalyzed) = await ResolveTableStatsAsync(connection, schema, table, cancellationToken);
        var columnStats = await ResolveColumnStatsAsync(connection, schema, table, rowCount, cancellationToken);

        return new TableStatisticsMetadata(rowCount, lastAnalyzed,
            columnStats.Count > 0 ? columnStats : null);
    }

    private static async Task<(long? rowCount, DateTimeOffset? lastAnalyzed)> ResolveTableStatsAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = TableStatsQuery;

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
            long? rowCount = reader.IsDBNull(0) ? null : reader.GetInt64(0);
            DateTimeOffset? lastAnalyzed = reader.IsDBNull(1)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(1);

            return (rowCount, lastAnalyzed);
        }

        return (null, null);
    }

    private static async Task<Dictionary<string, ColumnStatisticsMetadata>> ResolveColumnStatsAsync(
        DbConnection connection,
        string schema,
        string table,
        long? tableRowCount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ColumnStatsQuery;

        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@schema";
        schemaParam.Value = schema;
        command.Parameters.Add(schemaParam);

        var tableParam = command.CreateParameter();
        tableParam.ParameterName = "@table";
        tableParam.Value = table;
        command.Parameters.Add(tableParam);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<string, ColumnStatisticsMetadata>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var columnName = reader.GetString(0);

            double? nullFraction = reader.IsDBNull(1) ? null : reader.GetFloat(1);
            long? distinctCount = reader.IsDBNull(2)
                ? null
                : ComputeDistinctCount(reader.GetFloat(2), tableRowCount);

            var mcvText = reader.IsDBNull(3) ? null : reader.GetString(3);
            var mcfText = reader.IsDBNull(4) ? null : reader.GetString(4);
            var boundsText = reader.IsDBNull(5) ? null : reader.GetString(5);

            var mcv = ParsePgTextArray(mcvText);
            var mcf = ParsePgDoubleArray(mcfText);
            var bounds = ParsePgTextArray(boundsText);

            if (mcv is { Count: > MaxCommonValues })
            {
                mcv = mcv.Take(MaxCommonValues).ToList();
                mcf = mcf?.Take(MaxCommonValues).ToList();
            }

            string? minValue = bounds is { Count: > 0 } ? bounds[0] : null;
            string? maxValue = bounds is { Count: > 1 } ? bounds[^1] : null;

            if (nullFraction is null && distinctCount is null &&
                mcv is null && minValue is null && maxValue is null)
            {
                continue;
            }

            result[columnName] = new ColumnStatisticsMetadata(
                nullFraction,
                distinctCount,
                mcv is { Count: > 0 } ? mcv : null,
                mcf is { Count: > 0 } ? mcf : null,
                minValue,
                maxValue);
        }

        return result;
    }

    private static long? ComputeDistinctCount(float nDistinct, long? rowCount)
    {
        if (nDistinct >= 0)
            return (long)nDistinct;

        if (rowCount is null)
            return null;

        return (long)(Math.Abs(nDistinct) * rowCount.Value);
    }

    public static List<string>? ParsePgTextArray(string? arrayText)
    {
        if (string.IsNullOrEmpty(arrayText) || arrayText.Length < 2)
            return null;

        if (arrayText[0] != '{' || arrayText[^1] != '}')
            return null;

        var inner = arrayText.AsSpan(1, arrayText.Length - 2);
        if (inner.IsEmpty)
            return null;

        var values = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        var escaped = false;

        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];

            if (escaped)
            {
                current.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (c == ',' && !inQuote)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        values.Add(current.ToString());
        return values.Count > 0 ? values : null;
    }

    private static List<double>? ParsePgDoubleArray(string? arrayText)
    {
        var strings = ParsePgTextArray(arrayText);
        if (strings is null)
            return null;

        var result = new List<double>(strings.Count);
        foreach (var s in strings)
        {
            if (double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d))
                result.Add(d);
        }

        return result.Count > 0 ? result : null;
    }
}
