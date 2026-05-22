namespace PermitQL.Abstractions;

using System.Data.Common;
using Models;

public interface IStatisticsResolver
{
    ValueTask<TableStatisticsMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default);
}
