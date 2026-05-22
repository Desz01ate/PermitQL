using CommandLine;
using PermitQL;
using PermitQL.Abstractions;
using PermitQL.Server;
using PermitQL.Server.Models;
using System.Text;

await Parser.Default.ParseArguments<DiscoverOptions, ServeOptions>(args)
            .MapResult(
                (DiscoverOptions discover) => RunDiscoverAsync(discover),
                (ServeOptions serve) => RunServeAsync(serve),
                _ => Task.FromResult(1));

async Task<int> RunDiscoverAsync(DiscoverOptions discover)
{
    var options = StartupBootstrap.LoadOptions(allowMissingAppSettings: false);
    var connectionFactory = ConnectionFactory.Create(options.Provider, options.ConnectionString);
    var schemaDiscovery = SchemaDiscoveryFactory.Create(options.Provider, connectionFactory);
    await schemaDiscovery.DiscoverAsync(discover.Output, discover.Schemas.ToArray());
    Console.WriteLine($"Schema discovered and written to {discover.Output}");
    return 0;
}

async Task<int> RunServeAsync(ServeOptions serve)
{
    var options = StartupBootstrap.LoadOptions(allowMissingAppSettings: serve.Stdio);
    var rulesDirectory = StartupBootstrap.ResolveRulesDirectory(StartupBootstrap.BasePath, options.RulesDirectory);
    var connectionFactory = ConnectionFactory.Create(options.Provider, options.ConnectionString);
    var metadataResolvers = ProviderMetadataResolverFactory.Create(options.Provider);
    var validatorCapabilities = new ValidatorCapabilityDescriptor();

    if (serve.Stdio)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = StartupBootstrap.BasePath,
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(validatorCapabilities);
        builder.Services.AddPermitQL(rulesDirectory, connectionFactory, options.Provider,
            metadataResolvers.Constraints, metadataResolvers.Relationships,
            metadataResolvers.Indexes, metadataResolvers.Statistics, metadataResolvers.Capabilities);
        builder.Services
               .AddMcpServer()
               .WithStdioServerTransport()
               .WithToolsFromAssembly();
        await builder.Build().RunAsync();
    }
    else
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = StartupBootstrap.BasePath,
        });
        builder.Services.AddSingleton(validatorCapabilities);
        builder.Services.AddPermitQL(rulesDirectory, connectionFactory, options.Provider,
            metadataResolvers.Constraints, metadataResolvers.Relationships,
            metadataResolvers.Indexes, metadataResolvers.Statistics, metadataResolvers.Capabilities);
        builder.Services
               .AddMcpServer()
               .WithHttpTransport()
               .WithToolsFromAssembly();

        var app = builder.Build();
        app.MapMcp();

        app.MapPost("/api/query", async (QueryRequest request, IQueryPipeline pipeline, CancellationToken ct) =>
        {
            try
            {
                var result = await pipeline.ExecuteAsync(request.Query, request.RuleSetKey, ct);
                var response = new QueryResponse(
                    result.Columns.Select(c => new ColumnInfo(c.Name, c.Type, c.IsNullable)).ToList(),
                    result.Rows,
                    result.Rows.Count);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                var (message, type, statusCode) = ErrorHandler.Classify(ex);
                return Results.Json(new ErrorResponse(message, type), statusCode: statusCode);
            }
        });

        app.MapGet("/api/databases", (IRulesProvider rulesProvider) =>
        {
            return Results.Ok(rulesProvider.GetAvailableKeys());
        });

        app.MapGet("/api/databases/{key}", async (string key, IDataAccessor dataAccessor, IRulesProvider rulesProvider, IPermitQLFactory factory, ValidatorCapabilityDescriptor validatorCapabilities, CancellationToken ct) =>
        {
            try
            {
                var description = await PermitQL.Server.Tools.PermitQLTools.DescribeDatabase(dataAccessor, rulesProvider, factory, validatorCapabilities, key, ct);
                return Results.Content(description, "application/json");
            }
            catch (Exception ex)
            {
                var (message, type, statusCode) = ErrorHandler.Classify(ex);
                return Results.Json(new ErrorResponse(message, type), statusCode: statusCode);
            }
        });

        await app.RunAsync();
    }

    return 0;
}

public static class StartupBootstrap
{
    public static string BasePath => AppContext.BaseDirectory;
    private const string ConfigJsonEnvironmentVariable = "DATAGATEWAY_CONFIG_JSON";

    public static PermitQLOptions LoadOptions(bool allowMissingAppSettings, string? basePath = null)
    {
        var resolvedBasePath = basePath ?? BasePath;
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        var configBuilder = new ConfigurationBuilder()
                            .SetBasePath(resolvedBasePath)
                            .AddJsonFile("appsettings.json", optional: allowMissingAppSettings)
                            .AddJsonFile($"appsettings.{environmentName}.json", optional: true);

        var configJson = Environment.GetEnvironmentVariable(ConfigJsonEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configJson))
        {
            configBuilder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configJson)));
        }

        var config = configBuilder
                     .AddEnvironmentVariables()
                     .Build();

        return config.GetSection(PermitQLOptions.SectionName)
                     .Get<PermitQLOptions>()
               ?? throw new InvalidOperationException(
                   $"PermitQL configuration section is missing or invalid. Set '{ConfigJsonEnvironmentVariable}' or PermitQL environment variables (for example PermitQL__ConnectionString).");
    }

    public static string ResolveRulesDirectory(string basePath, string rulesDirectory)
    {
        var resolvedPath = Path.GetFullPath(rulesDirectory, basePath);

        if (!Directory.Exists(resolvedPath))
            throw new DirectoryNotFoundException($"Rules directory '{rulesDirectory}' was not found. Resolved path: '{resolvedPath}'.");

        return resolvedPath;
    }
}
