namespace PermitQL.Abstractions;

using System.Data.Common;
using Models;

public interface IRelationshipResolver
{
    ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveOutboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveInboundAsync(
        DbConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken = default);
}
