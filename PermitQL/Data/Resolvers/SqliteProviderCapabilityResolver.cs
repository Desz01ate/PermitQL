namespace PermitQL.Data.Resolvers;

using Abstractions;
using Models;

public sealed class SqliteProviderCapabilityResolver : IProviderCapabilityResolver
{
    private static readonly QueryCapabilityMetadata Capabilities = new(
        Ctes: CapabilitySupport.Supported,
        Subqueries: CapabilitySupport.Supported,
        DerivedTables: CapabilitySupport.Supported,
        WindowFunctions: CapabilitySupport.Supported,
        Mutations: CapabilitySupport.Supported,
        Notes:
        [
            "Provider capabilities reflect modern SQLite dialect support.",
        ]);

    public ValueTask<QueryCapabilityMetadata> ResolveAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Capabilities);
}
