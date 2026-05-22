namespace PermitQL.Abstractions;

using System.Data.Common;
using Models;

public interface IForeignKeyResolver
{
    ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default);
}
