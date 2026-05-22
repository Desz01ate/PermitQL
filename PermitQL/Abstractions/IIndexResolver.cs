namespace PermitQL.Abstractions;

using System.Data.Common;
using Models;

public interface IIndexResolver
{
    ValueTask<IReadOnlyList<TableIndexMetadata>> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default);
}
