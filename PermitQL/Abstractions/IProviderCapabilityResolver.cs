namespace PermitQL.Abstractions;

using Models;

public interface IProviderCapabilityResolver
{
    ValueTask<QueryCapabilityMetadata> ResolveAsync(CancellationToken cancellationToken = default);
}
