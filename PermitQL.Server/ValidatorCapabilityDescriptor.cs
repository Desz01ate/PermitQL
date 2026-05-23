namespace PermitQL.Server;

using PermitQL.Models;

public sealed class ValidatorCapabilityDescriptor
{
    public QueryCapabilityMetadata Describe()
    {
        return new QueryCapabilityMetadata(
            Ctes: CapabilitySupport.Supported,
            Subqueries: CapabilitySupport.Supported,
            DerivedTables: CapabilitySupport.Supported,
            WindowFunctions: CapabilitySupport.Unknown,
            Mutations: CapabilitySupport.Supported,
            Notes:
            [
                "Capability states reflect gateway validation behavior, not only raw SQL dialect support.",
            ]);
    }
}
