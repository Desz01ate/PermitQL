namespace PermitQL.Data;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class NullConstraintResolver : IConstraintResolver
{
    public static readonly NullConstraintResolver Instance = new();

    public ValueTask<TableConstraintMetadata> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new TableConstraintMetadata([], []));
}
