namespace PermitQL.Data;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class NullForeignKeyResolver : IForeignKeyResolver
{
    public static readonly NullForeignKeyResolver Instance = new();

    public ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<ForeignKeyMetadata>>([]);
}
