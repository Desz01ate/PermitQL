using CommandLine;

namespace PermitQL.Server;

[Verb("discover", HelpText = "Discover database schema and write to file.")]
public sealed class DiscoverOptions
{
    [Option('o', "output", Default = "discovered_schema.yaml", HelpText = "Output file path for discovered schema.")]
    public string Output { get; set; } = "discovered_schema.yaml";

    [Option('s', "schema", HelpText = "Schema names to include in discovery.")]
    public IEnumerable<string> Schemas { get; set; } = [];
}

[Verb("serve", isDefault: true, HelpText = "Run the PermitQL server.")]
public sealed class ServeOptions
{
    [Option("stdio", Default = false, HelpText = "Use stdio transport instead of HTTP.")]
    public bool Stdio { get; set; }
}