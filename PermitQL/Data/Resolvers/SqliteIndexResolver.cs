namespace PermitQL.Data.Resolvers;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class SqliteIndexResolver : IIndexResolver
{
    public async ValueTask<IReadOnlyList<TableIndexMetadata>> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        var indexEntries = new List<(string Name, bool IsUnique)>();

        await using (var listCmd = connection.CreateCommand())
        {
            listCmd.CommandText = $"PRAGMA index_list(\"{table.Replace("\"", "\"\"")}\")";
            await using var listReader = await listCmd.ExecuteReaderAsync(cancellationToken);

            while (await listReader.ReadAsync(cancellationToken))
            {
                var name = listReader.GetString(1);
                var isUnique = listReader.GetInt64(2) == 1;
                var origin = listReader.GetString(3);

                if (origin == "pk")
                {
                    continue;
                }

                indexEntries.Add((name, isUnique));
            }
        }

        var results = new List<TableIndexMetadata>();

        foreach (var (name, isUnique) in indexEntries)
        {
            var columns = new List<string>();
            await using var infoCmd = connection.CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info(\"{name.Replace("\"", "\"\"")}\")";
            await using var infoReader = await infoCmd.ExecuteReaderAsync(cancellationToken);

            while (await infoReader.ReadAsync(cancellationToken))
            {
                columns.Add(infoReader.GetString(2));
            }

            results.Add(new TableIndexMetadata(name, columns, isUnique));
        }

        return results;
    }
}
