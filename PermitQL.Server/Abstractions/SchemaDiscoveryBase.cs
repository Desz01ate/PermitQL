namespace PermitQL.Server.Abstractions;

using System.Data.Common;
using System.Text;

public abstract class SchemaDiscoveryBase : ISchemaDiscovery
{
    readonly protected static HashSet<string> SystemSchemas = new(StringComparer.OrdinalIgnoreCase)
    {
        "information_schema",
        "pg_catalog",
        "pg_toast",
    };

    private readonly Func<DbConnection> _connectionFactory;

    public SchemaDiscoveryBase(Func<DbConnection> connectionFactory)
    {
        this._connectionFactory = connectionFactory;
    }

    public async Task DiscoverAsync(string outputPath, string[] schemaFilters)
    {
        await using var connection = this._connectionFactory();
        await connection.OpenAsync();

        var tables = await this.GetTablesAsync(connection, schemaFilters);
        var columnsByTable = await this.GetColumnsAsync(connection, tables);

        var yaml = BuildYaml(connection.Database, tables, columnsByTable);
        await File.WriteAllTextAsync(outputPath, yaml);
    }

    protected abstract Task<List<(string Schema, string Table)>> GetTablesAsync(DbConnection connection, string[] schemaFilters);

    protected abstract Task<Dictionary<(string Schema, string Table), List<string>>> GetColumnsAsync(DbConnection connection, List<(string Schema, string Table)> tables);

    private static string BuildYaml(
        string database,
        List<(string Schema, string Table)> tables,
        Dictionary<(string Schema, string Table), List<string>> columnsByTable)
    {
        var sb = new StringBuilder();
        sb.AppendLine("version: \"1.0\"");
        sb.AppendLine($"database: \"{database}\"");
        sb.AppendLine("global_limits:");
        sb.AppendLine("  max_rows_returned: 100");
        sb.AppendLine("  timeout_ms: 5000");
        sb.AppendLine("  allowed_operations: [select] # supported: select, insert, update, delete");
        sb.AppendLine();
        sb.AppendLine("exposed_schemas:");

        foreach (var group in tables.GroupBy(t => t.Schema))
        {
            sb.AppendLine($"  {group.Key}:");
            sb.AppendLine("    tables:");

            foreach (var (schema, table) in group.OrderBy(t => t.Table))
            {
                sb.AppendLine($"      {table}:");

                var columns = columnsByTable.GetValueOrDefault((schema, table), []);
                var comment = columns.Count > 0 ? $" # {string.Join(", ", columns)}" : "";
                sb.AppendLine($"        allowed_columns: [\"*\"]{comment}");
                sb.AppendLine($"        # denied_columns: []    # excludes columns when allowed_columns is [\"*\"]");
                sb.AppendLine($"        # row_filter: \"\"        # SQL predicate injected into WHERE/ON clauses");
                sb.AppendLine($"        # allowed_operations: [] # overrides global; supported: select, insert, update, delete");
            }
        }

        return sb.ToString();
    }
}