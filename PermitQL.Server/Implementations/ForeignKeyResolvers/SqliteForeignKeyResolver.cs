namespace PermitQL.Server.Implementations.ForeignKeyResolvers;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Models;

public sealed class SqliteForeignKeyResolver : IForeignKeyResolver
{
    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list(\"{table}\")";

        var results = new List<ForeignKeyMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var referencedTable = reader.GetString(2);
            var fromColumn = reader.GetString(3);
            var toColumn = reader.GetString(4);

            results.Add(new ForeignKeyMetadata(
                ConstraintName: $"fk_{table}_{id}",
                FromSchema: schema,
                FromTable: table,
                FromColumn: fromColumn,
                ToSchema: "main",
                ToTable: referencedTable,
                ToColumn: toColumn,
                OnDelete: null,
                OnUpdate: null));
        }

        return results;
    }
}
