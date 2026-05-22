namespace PermitQL.Tests.Server;

using PermitQL.Models;
using PermitQL.Server;

public sealed class ValidatorCapabilityDescriptorTests
{
    [Fact]
    public void Describe_ReturnsExplicitCapabilityStates()
    {
        var descriptor = new ValidatorCapabilityDescriptor();

        var capabilities = descriptor.Describe();

        Assert.Equal(CapabilitySupport.Unsupported, capabilities.Ctes);
        Assert.Equal(CapabilitySupport.Supported,   capabilities.Subqueries);
        Assert.Equal(CapabilitySupport.Supported,   capabilities.DerivedTables);
        Assert.Equal(CapabilitySupport.Unknown,     capabilities.WindowFunctions);
        Assert.Equal(CapabilitySupport.Supported,   capabilities.Mutations);
        Assert.NotNull(capabilities.Notes);
    }
}
