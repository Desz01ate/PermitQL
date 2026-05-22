namespace PermitQL.Tests.Server;

using PermitQL.Abstractions;
using PermitQL.Server;

public sealed class StartupBootstrapTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    public StartupBootstrapTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        RestoreEnvironmentVariable("PermitQL__RulesDirectory");
        RestoreEnvironmentVariable("PermitQL__ConnectionString");
        RestoreEnvironmentVariable("PermitQL__Provider");
        RestoreEnvironmentVariable("DATAGATEWAY_CONFIG_JSON");

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ResolveRulesDirectory_WhenRelative_ReturnsAbsolutePathUnderBaseDirectory()
    {
        var rulesDir = Path.Combine(_tempDir, "Rules");
        Directory.CreateDirectory(rulesDir);

        var resolved = StartupBootstrap.ResolveRulesDirectory(_tempDir, "Rules");

        Assert.Equal(rulesDir, resolved);
    }

    [Fact]
    public void ResolveRulesDirectory_WhenDirectoryDoesNotExist_ThrowsClearError()
    {
        var ex = Assert.Throws<DirectoryNotFoundException>(
            () => StartupBootstrap.ResolveRulesDirectory(_tempDir, "MissingRules"));

        Assert.Contains("MissingRules", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderMetadataResolverFactory_ForSqlite_ReturnsConcreteResolvers()
    {
        var resolvers = ProviderMetadataResolverFactory.Create(SqlDialect.Sqlite);

        Assert.NotNull(resolvers.Constraints);
        Assert.NotNull(resolvers.Relationships);
        Assert.NotNull(resolvers.Indexes);
        Assert.NotNull(resolvers.Statistics);
        Assert.NotNull(resolvers.Capabilities);
    }

    [Fact]
    public void LoadOptions_WhenAppSettingsMissingAndAllowMissing_UsesEnvironmentVariables()
    {
        var rulesDir = Path.Combine(_tempDir, "Rules");
        Directory.CreateDirectory(rulesDir);
        SetEnvironmentVariable("PermitQL__RulesDirectory", rulesDir);
        SetEnvironmentVariable("PermitQL__ConnectionString", "Data Source=:memory:");
        SetEnvironmentVariable("PermitQL__Provider", "sqlite");

        var options = StartupBootstrap.LoadOptions(allowMissingAppSettings: true, basePath: _tempDir);

        Assert.Equal(rulesDir, options.RulesDirectory);
        Assert.Equal("Data Source=:memory:", options.ConnectionString);
        Assert.Equal(SqlDialect.Sqlite, options.Provider);
    }

    [Fact]
    public void LoadOptions_WhenConfigJsonEnvironmentVariableSet_UsesJsonPayload()
    {
        var rulesDir = Path.Combine(_tempDir, "RulesJson");
        Directory.CreateDirectory(rulesDir);
        SetEnvironmentVariable("DATAGATEWAY_CONFIG_JSON", $$"""
                                                           {
                                                             "PermitQL": {
                                                               "RulesDirectory": "{{rulesDir}}",
                                                               "ConnectionString": "Data Source={{Path.Combine(_tempDir, "test.db")}}",
                                                               "Provider": "sqlite"
                                                             }
                                                           }
                                                           """);

        var options = StartupBootstrap.LoadOptions(allowMissingAppSettings: true, basePath: _tempDir);

        Assert.Equal(rulesDir, options.RulesDirectory);
        Assert.Contains("test.db", options.ConnectionString, StringComparison.Ordinal);
        Assert.Equal(SqlDialect.Sqlite, options.Provider);
    }

    private void SetEnvironmentVariable(string key, string value)
    {
        if (!_originalEnvironment.ContainsKey(key))
            _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);

        Environment.SetEnvironmentVariable(key, value);
    }

    private void RestoreEnvironmentVariable(string key)
    {
        if (_originalEnvironment.TryGetValue(key, out var originalValue))
            Environment.SetEnvironmentVariable(key, originalValue);
        else
            Environment.SetEnvironmentVariable(key, null);
    }
}
