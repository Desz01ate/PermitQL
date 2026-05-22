namespace PermitQL.Data;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class NullStatisticsResolver : IStatisticsResolver
{
    public static readonly NullStatisticsResolver Instance = new();

    public ValueTask<TableStatisticsMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new TableStatisticsMetadata(null, null));
}
