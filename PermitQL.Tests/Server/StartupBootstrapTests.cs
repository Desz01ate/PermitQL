namespace PermitQL.Tests.Server;

using PermitQL;
using PermitQL.Abstractions;

public sealed class StartupBootstrapTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    public StartupBootstrapTests()
    {
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        this.RestoreEnvironmentVariable("PermitQL__RulesDirectory");
        this.RestoreEnvironmentVariable("PermitQL__ConnectionString");
        this.RestoreEnvironmentVariable("PermitQL__Provider");
        this.RestoreEnvironmentVariable("PERMITQL_CONFIG_JSON");

        if (Directory.Exists(this._tempDir))
            Directory.Delete(this._tempDir, true);
    }

    [Fact]
    public void ResolveRulesDirectory_WhenRelative_ReturnsAbsolutePathUnderBaseDirectory()
    {
        var rulesDir = Path.Combine(this._tempDir, "Rules");
        Directory.CreateDirectory(rulesDir);

        var resolved = StartupBootstrap.ResolveRulesDirectory(this._tempDir, "Rules");

        Assert.Equal(rulesDir, resolved);
    }

    [Fact]
    public void ResolveRulesDirectory_WhenDirectoryDoesNotExist_ThrowsClearError()
    {
        var ex = Assert.Throws<DirectoryNotFoundException>(
            () => StartupBootstrap.ResolveRulesDirectory(this._tempDir, "MissingRules"));

        Assert.Contains("MissingRules", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PermitQLFactory_ForSqlite_CreatesSupportedResolvers()
    {
        var factory = new PermitQLFactory(SqlDialect.Sqlite);

        Assert.NotNull(factory.CreateForeignKeyResolver());
        Assert.NotNull(factory.CreateConstraintResolver());
        Assert.NotNull(factory.CreateRelationshipResolver());
        Assert.NotNull(factory.CreateIndexResolver());
        Assert.NotNull(factory.CreateStatisticsResolver());
        Assert.NotNull(factory.CreateProviderCapabilityResolver());
    }

    [Fact]
    public void LoadOptions_WhenAppSettingsMissingAndAllowMissing_UsesEnvironmentVariables()
    {
        var rulesDir = Path.Combine(this._tempDir, "Rules");
        Directory.CreateDirectory(rulesDir);
        this.SetEnvironmentVariable("PermitQL__RulesDirectory", rulesDir);
        this.SetEnvironmentVariable("PermitQL__ConnectionString", "Data Source=:memory:");
        this.SetEnvironmentVariable("PermitQL__Provider", "sqlite");

        var options = StartupBootstrap.LoadOptions(allowMissingAppSettings: true, basePath: this._tempDir);

        Assert.Equal(rulesDir, options.RulesDirectory);
        Assert.Equal("Data Source=:memory:", options.ConnectionString);
        Assert.Equal(SqlDialect.Sqlite, options.Provider);
    }

    [Fact]
    public void LoadOptions_WhenConfigJsonEnvironmentVariableSet_UsesJsonPayload()
    {
        var rulesDir = Path.Combine(this._tempDir, "RulesJson");
        Directory.CreateDirectory(rulesDir);
        this.SetEnvironmentVariable("PERMITQL_CONFIG_JSON", $$"""
                                                              {
                                                                "PermitQL": {
                                                                  "RulesDirectory": "{{rulesDir}}",
                                                                  "ConnectionString": "Data Source={{Path.Combine(this._tempDir, "test.db")}}",
                                                                  "Provider": "sqlite"
                                                                }
                                                              }
                                                              """);

        var options = StartupBootstrap.LoadOptions(allowMissingAppSettings: true, basePath: this._tempDir);

        Assert.Equal(rulesDir, options.RulesDirectory);
        Assert.Contains("test.db", options.ConnectionString, StringComparison.Ordinal);
        Assert.Equal(SqlDialect.Sqlite, options.Provider);
    }

    private void SetEnvironmentVariable(string key, string value)
    {
        if (!this._originalEnvironment.ContainsKey(key)) this._originalEnvironment[key] = Environment.GetEnvironmentVariable(key);

        Environment.SetEnvironmentVariable(key, value);
    }

    private void RestoreEnvironmentVariable(string key)
    {
        if (this._originalEnvironment.TryGetValue(key, out var originalValue))
            Environment.SetEnvironmentVariable(key, originalValue);
        else
            Environment.SetEnvironmentVariable(key, null);
    }
}
