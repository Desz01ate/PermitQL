namespace PermitQL.Data;

using System.Data.Common;
using Abstractions;
using Models;

public sealed class NullRelationshipResolver : IRelationshipResolver
{
    public static readonly NullRelationshipResolver Instance = new();

    public ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveOutboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<ForeignKeyMetadata>>([]);

    public ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveInboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<ForeignKeyMetadata>>([]);
}
