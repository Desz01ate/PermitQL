namespace PermitQL.Data;

using Abstractions;
using Models;

public sealed class NullProviderCapabilityResolver : IProviderCapabilityResolver
{
    public static readonly NullProviderCapabilityResolver Instance = new();

    public ValueTask<QueryCapabilityMetadata> ResolveAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new QueryCapabilityMetadata(
            CapabilitySupport.Unknown,
            CapabilitySupport.Unknown,
            CapabilitySupport.Unknown,
            CapabilitySupport.Unknown,
            CapabilitySupport.Unknown,
            []));
}
