namespace PermitQL.Tests.Rules;

using PermitQL.Rules;

public class YamlRulesProviderTests : IDisposable
{
    private readonly string _tempDir;

    public YamlRulesProviderTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDir))
            Directory.Delete(this._tempDir, true);
    }

    private void WriteRuleFile(string filename, string content)
    {
        File.WriteAllText(Path.Combine(this._tempDir, filename), content);
    }

    [Fact]
    public void GetRuleSet_LoadsByDatabaseKey()
    {
        this.WriteRuleFile("prod.yaml", """
                                        version: "1.0"
                                        database: "production"
                                        global_limits:
                                          max_rows_returned: 100
                                          timeout_ms: 1000
                                          allowed_operations: [select]
                                        exposed_schemas:
                                          public:
                                            tables:
                                              users:
                                                allowed_columns: ["id", "name"]
                                        """);
        var provider = new YamlRulesProvider(this._tempDir);
        var ruleSet = provider.GetRuleSet("production");
        Assert.Equal("production", ruleSet.Database);
        Assert.Equal(100, ruleSet.GlobalLimits.MaxRowsReturned);
    }

    [Fact]
    public void GetAvailableKeys_ReturnsAllLoadedDatabases()
    {
        this.WriteRuleFile("prod.yaml", """
                                        version: "1.0"
                                        database: "production"
                                        global_limits:
                                          max_rows_returned: 100
                                          timeout_ms: 1000
                                          allowed_operations: [select]
                                        exposed_schemas: {}
                                        """);
        this.WriteRuleFile("staging.yaml", """
                                           version: "1.0"
                                           database: "staging"
                                           global_limits:
                                             max_rows_returned: 500
                                             timeout_ms: 5000
                                             allowed_operations: [select, insert, update, delete]
                                           exposed_schemas: {}
                                           """);
        var provider = new YamlRulesProvider(this._tempDir);
        var keys = provider.GetAvailableKeys();
        Assert.Contains("production", keys);
        Assert.Contains("staging", keys);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void GetRuleSet_UnknownKey_ThrowsKeyNotFoundException()
    {
        this.WriteRuleFile("prod.yaml", """
                                        version: "1.0"
                                        database: "production"
                                        global_limits:
                                          max_rows_returned: 100
                                          timeout_ms: 1000
                                          allowed_operations: [select]
                                        exposed_schemas: {}
                                        """);
        var provider = new YamlRulesProvider(this._tempDir);
        Assert.Throws<KeyNotFoundException>(() => provider.GetRuleSet("nonexistent"));
    }

    [Fact]
    public void Constructor_EmptyDirectory_LoadsNoRules()
    {
        var provider = new YamlRulesProvider(this._tempDir);
        Assert.Empty(provider.GetAvailableKeys());
    }

    [Fact]
    public void Constructor_InvalidYaml_ThrowsOnStartup()
    {
        this.WriteRuleFile("bad.yaml", "this is not valid yaml: [[[");
        Assert.ThrowsAny<Exception>(() => new YamlRulesProvider(this._tempDir));
    }

    [Fact]
    public void GetRuleSet_LoadsAllowedOperations_AtGlobalAndTableLevel()
    {
        this.WriteRuleFile("app.yaml", """
                                       version: "1.0"
                                       database: "application"
                                       global_limits:
                                         max_rows_returned: 100
                                         timeout_ms: 1000
                                         allowed_operations: [select, insert]
                                       exposed_schemas:
                                         public:
                                           tables:
                                             products:
                                               allowed_columns: ["*"]
                                               allowed_operations: [select]
                                             orders:
                                               allowed_columns: ["*"]
                                       """);
        var provider = new YamlRulesProvider(this._tempDir);
        var ruleSet = provider.GetRuleSet("application");

        Assert.Equal(["select", "insert"], ruleSet.GlobalLimits.AllowedOperations);
        Assert.Equal(["select"], ruleSet.ExposedSchemas["public"].Tables["products"].AllowedOperations!);
        Assert.Null(ruleSet.ExposedSchemas["public"].Tables["orders"].AllowedOperations);
    }

    [Fact]
    public void GetRuleSet_LoadsTableAndColumnSemanticDescriptions()
    {
        this.WriteRuleFile("app.yaml", """
                                       version: "1.0"
                                       database: "application"
                                       global_limits:
                                         max_rows_returned: 100
                                         timeout_ms: 1000
                                         allowed_operations: [select]
                                       exposed_schemas:
                                         public:
                                           tables:
                                             products:
                                               allowed_columns: ["id", "name"]
                                               table_semantic_description: "Products available for sale"
                                               column_semantic_descriptions:
                                                 id: "Product identifier"
                                                 name: "Product display name"
                                       """);

        var provider = new YamlRulesProvider(this._tempDir);
        var ruleSet = provider.GetRuleSet("application");
        var tableRule = ruleSet.ExposedSchemas["public"].Tables["products"];

        Assert.Equal("Products available for sale", tableRule.TableSemanticDescription);
        Assert.Equal("Product identifier", tableRule.ColumnSemanticDescriptions["id"]);
        Assert.Equal("Product display name", tableRule.ColumnSemanticDescriptions["name"]);
        Assert.Equal(2, tableRule.ColumnSemanticDescriptions.Count);
    }
}
