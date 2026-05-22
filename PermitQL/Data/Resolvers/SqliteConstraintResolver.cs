namespace PermitQL.Data.Resolvers;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class SqliteConstraintResolver : IConstraintResolver
{
    public async ValueTask<TableConstraintMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        var unique = await ResolveUniqueConstraintsAsync(connection, table, cancellationToken);
        var check = await ResolveCheckConstraintsAsync(connection, table, cancellationToken);
        return new TableConstraintMetadata(unique, check);
    }

    private static async Task<List<UniqueConstraintMetadata>> ResolveUniqueConstraintsAsync(
        DbConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var results = new List<UniqueConstraintMetadata>();

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list(\"{table.Replace("\"", "\"\"")}\")";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var indexEntries = new List<(string Name, string Origin)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(1);
            var isUnique = reader.GetInt64(2) == 1;
            var origin = reader.GetString(3);

            if (isUnique && origin == "u")
            {
                indexEntries.Add((name, origin));
            }
        }

        foreach (var (name, _) in indexEntries)
        {
            var columns = new List<string>();
            await using var infoCmd = connection.CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info(\"{name.Replace("\"", "\"\"")}\")";
            await using var infoReader = await infoCmd.ExecuteReaderAsync(cancellationToken);

            while (await infoReader.ReadAsync(cancellationToken))
            {
                columns.Add(infoReader.GetString(2));
            }

            results.Add(new UniqueConstraintMetadata(name, columns));
        }

        return results;
    }

    private static async Task<List<CheckConstraintMetadata>> ResolveCheckConstraintsAsync(
        DbConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var results = new List<CheckConstraintMetadata>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@table";

        var param = command.CreateParameter();
        param.ParameterName = "@table";
        param.Value = table;
        command.Parameters.Add(param);

        var sql = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrEmpty(sql))
            return results;

        var extracted = ExtractCheckConstraints(sql);
        for (var i = 0; i < extracted.Count; i++)
        {
            results.Add(new CheckConstraintMetadata($"ck_{table}_{i}", extracted[i]));
        }

        return results;
    }

    private static List<string> ExtractCheckConstraints(string ddl)
    {
        var results = new List<string>();
        var upper = ddl.ToUpperInvariant();
        var idx = 0;
        while ((idx = upper.IndexOf("CHECK", idx, StringComparison.Ordinal)) >= 0)
        {
            idx += 5;
            while (idx < ddl.Length && char.IsWhiteSpace(ddl[idx]))
            {
                idx++;
            }

            if (idx >= ddl.Length || ddl[idx] != '(')
            {
                continue;
            }

            var depth = 0;
            var start = idx + 1;
            for (var i = idx; i < ddl.Length; i++)
            {
                if (ddl[i] == '(')
                {
                    depth++;
                }
                else if (ddl[i] == ')')
                {
                    depth--;
                }

                if (depth == 0)
                {
                    results.Add(ddl[start..i].Trim());
                    idx = i + 1;
                    break;
                }
            }
        }

        return results;
    }
}
