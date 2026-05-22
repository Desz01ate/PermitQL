namespace PermitQL.Data;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class NullIndexResolver : IIndexResolver
{
    public static readonly NullIndexResolver Instance = new();

    public ValueTask<IReadOnlyList<TableIndexMetadata>> ResolveAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<TableIndexMetadata>>([]);
}
