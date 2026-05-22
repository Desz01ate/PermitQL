namespace PermitQL.Data.Resolvers;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class SqliteStatisticsResolver : IStatisticsResolver
{
    public ValueTask<TableStatisticsMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new TableStatisticsMetadata(null, null));
}
