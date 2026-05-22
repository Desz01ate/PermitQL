namespace PermitQL.Server.Implementations.DiscoveryService;

using System.Data.Common;
using Abstractions;

public class SqliteDiscovery : SchemaDiscoveryBase
{
    public SqliteDiscovery(Func<DbConnection> connectionFactory)
        : base(connectionFactory)
    {
    }

    protected override async Task<List<(string Schema, string Table)>> GetTablesAsync(DbConnection connection, string[] schemaFilters)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT 'main', name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

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

        foreach (var (schema, table) in tables)
        {
            command.CommandText = $"PRAGMA table_info(\"{table}\")";
            var columns = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(1));
            result[(schema, table)] = columns;
        }

        return result;
    }
}