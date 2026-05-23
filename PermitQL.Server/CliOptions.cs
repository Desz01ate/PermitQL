using CommandLine;

namespace PermitQL.Server;

using PermitQL.Abstractions;

[Verb("discover", HelpText = "Discover database schema and write to file.")]
public sealed class DiscoverOptions
{
    [Option('o', "output", Default = "discovered_schema.yaml", HelpText = "Output file path for discovered schema.")]
    public string Output { get; init; } = "discovered_schema.yaml";

    [Option('s', "schema", HelpText = "Schema names to include in discovery.")]
    public IEnumerable<string> Schemas { get; init; } = [];

    [Option('p', "provider", HelpText = "Database provider to use ('postgresql', 'sqlite').", Required = true)]
    public string ProviderString { get; init; } = null!;

    public SqlDialect Provider => Enum.Parse<SqlDialect>(this.ProviderString, ignoreCase: true);

    [Option('c', "connection-string", HelpText = "Database connection string.", Required = true)]
    public string ConnectionString { get; init; } = null!;
}

[Verb("serve", isDefault: true, HelpText = "Run the PermitQL server.")]
public sealed class ServeOptions
{
    [Option("stdio", Default = false, HelpText = "Use stdio transport instead of HTTP.")]
    public bool Stdio { get; init; }
}