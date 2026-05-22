namespace PermitQL.Server.Implementations.DiscoveryService;

using System.Data.Common;
using Abstractions;

public class PostgresqlDiscovery : SchemaDiscoveryBase
{
    public PostgresqlDiscovery(Func<DbConnection> connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override async Task<List<(string Schema, string Table)>> GetTablesAsync(DbConnection connection, string[] schemaFilters)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT table_schema, table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' ORDER BY table_schema, table_name";

        var tables = new List<(string Schema, string Table)>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);

            if (schemaFilters.Length > 0)
            {
                if (!schemaFilters.Contains(schema, StringComparer.OrdinalIgnoreCase))
                    continue;
            }
            else if (SystemSchemas.Contains(schema))
            {
                continue;
            }

            tables.Add((schema, name));
        }

        return tables;
    }

    protected override async Task<Dictionary<(string Schema, string Table), List<string>>> GetColumnsAsync(DbConnection connection, List<(string Schema, string Table)> tables)
    {
        if (tables.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();

        var result = new Dictionary<(string Schema, string Table), List<string>>();

        command.CommandText = "SELECT table_schema, table_name, column_name FROM information_schema.columns ORDER BY table_schema, table_name, ordinal_position";
        var all = new Dictionary<(string, string), List<string>>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var key = (reader.GetString(0), reader.GetString(1));

            if (!all.TryGetValue(key, out var list))
            {
                list = [];
                all[key] = list;
            }

            list.Add(reader.GetString(2));
        }

        foreach (var (schema, table) in tables)
        {
            if (all.TryGetValue((schema, table), out var columns))
                result[(schema, table)] = columns;
        }

        return result;
    }
}