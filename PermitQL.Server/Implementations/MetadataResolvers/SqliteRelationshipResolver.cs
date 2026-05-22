namespace PermitQL.Server.Implementations.MetadataResolvers;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Models;

public sealed class SqliteRelationshipResolver : IRelationshipResolver
{
    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveOutboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list(\"{table.Replace("\"", "\"\"")}\")";

        var results = new List<ForeignKeyMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var referencedTable = reader.GetString(2);
            var fromColumn = reader.GetString(3);
            var toColumn = reader.GetString(4);
            var onUpdate = reader.GetString(5);
            var onDelete = reader.GetString(6);

            results.Add(new ForeignKeyMetadata(
                ConstraintName: $"fk_{table}_{id}",
                FromSchema: schema,
                FromTable: table,
                FromColumn: fromColumn,
                ToSchema: schema,
                ToTable: referencedTable,
                ToColumn: toColumn,
                OnDelete: NormalizeAction(onDelete),
                OnUpdate: NormalizeAction(onUpdate)));
        }

        return results;
    }

    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveInboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        var allTables = new List<string>();

        await using (var listCmd = connection.CreateCommand())
        {
            listCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name != @table";

            var param = listCmd.CreateParameter();
            param.ParameterName = "@table";
            param.Value = table;
            listCmd.Parameters.Add(param);

            await using var listReader = await listCmd.ExecuteReaderAsync(cancellationToken);
            while (await listReader.ReadAsync(cancellationToken))
            {
                allTables.Add(listReader.GetString(0));
            }
        }

        var results = new List<ForeignKeyMetadata>();

        foreach (var candidateTable in allTables)
        {
            await using var fkCmd = connection.CreateCommand();
            fkCmd.CommandText = $"PRAGMA foreign_key_list(\"{candidateTable.Replace("\"", "\"\"")}\")";
            await using var fkReader = await fkCmd.ExecuteReaderAsync(cancellationToken);

            while (await fkReader.ReadAsync(cancellationToken))
            {
                var id = fkReader.GetInt64(0);
                var referencedTable = fkReader.GetString(2);

                if (!string.Equals(referencedTable, table, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fromColumn = fkReader.GetString(3);
                var toColumn = fkReader.GetString(4);
                var onUpdate = fkReader.GetString(5);
                var onDelete = fkReader.GetString(6);

                results.Add(new ForeignKeyMetadata(
                    ConstraintName: $"fk_{candidateTable}_{id}",
                    FromSchema: schema,
                    FromTable: candidateTable,
                    FromColumn: fromColumn,
                    ToSchema: schema,
                    ToTable: referencedTable,
                    ToColumn: toColumn,
                    OnDelete: NormalizeAction(onDelete),
                    OnUpdate: NormalizeAction(onUpdate)));
            }
        }

        return results;
    }

    private static string? NormalizeAction(string action)
        => string.Equals(action, "NO ACTION", StringComparison.OrdinalIgnoreCase) ? null : action;
}
