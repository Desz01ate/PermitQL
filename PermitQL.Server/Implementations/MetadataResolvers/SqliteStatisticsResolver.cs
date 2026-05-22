namespace PermitQL.Server.Implementations.MetadataResolvers;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Models;

public sealed class SqliteStatisticsResolver : IStatisticsResolver
{
    public ValueTask<TableStatisticsMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new TableStatisticsMetadata(null, null));
}
