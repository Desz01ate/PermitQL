namespace PermitQL.Server.Abstractions;


public interface ISchemaDiscovery
{
    public Task DiscoverAsync(string outputPath, string[] schemaFilters);
}