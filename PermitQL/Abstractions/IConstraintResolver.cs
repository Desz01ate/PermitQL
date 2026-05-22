namespace PermitQL.Abstractions;

using System.Data.Common;
using Models;

public interface IConstraintResolver
{
    ValueTask<TableConstraintMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default);
}
