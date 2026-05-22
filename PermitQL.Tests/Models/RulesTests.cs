namespace PermitQL.Tests.Models;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using PermitQL.Models;

public class RulesTests
{
    private static RuleSet DeserializeYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
                           .WithNamingConvention(UnderscoredNamingConvention.Instance)
                           .Build();
        return deserializer.Deserialize<RuleSet>(yaml);
    }

    [Fact]
    public void Deserialize_FullRuleSet_MapsAllProperties()
    {
        var yaml = """
                   version: "1.0"
                   database: "production_read_replica"
                   global_limits:
                     max_rows_returned: 250
                     timeout_ms: 2000
                     allowed_operations: [select]
                   exposed_schemas:
                     public:
                       tables:
                         products:
                           allowed_columns: ["id", "name", "price"]
                           row_filter: "is_active = true"
                         orders:
                           allowed_columns: ["id", "total_amount"]
                   """;

        var ruleSet = DeserializeYaml(yaml);

        Assert.Equal("1.0", ruleSet.Version);
        Assert.Equal("production_read_replica", ruleSet.Database);
        Assert.Equal(250, ruleSet.GlobalLimits.MaxRowsReturned);
        Assert.Equal(2000, ruleSet.GlobalLimits.TimeoutMs);
        Assert.Equal(["select"], ruleSet.GlobalLimits.AllowedOperations);
        Assert.Equal(2, ruleSet.ExposedSchemas["public"].Tables.Count);
        Assert.Equal(new[] { "id", "name", "price" }, ruleSet.ExposedSchemas["public"].Tables["products"].AllowedColumns);
        Assert.Equal("is_active = true", ruleSet.ExposedSchemas["public"].Tables["products"].RowFilter);
        Assert.Null(ruleSet.ExposedSchemas["public"].Tables["orders"].RowFilter);
    }

    [Fact]
    public void Deserialize_WildcardColumns_WithDeniedColumns()
    {
        var yaml = """
                   version: "1.0"
                   database: "test"
                   global_limits:
                     max_rows_returned: 100
                     timeout_ms: 1000
                     allowed_operations: [select]
                   exposed_schemas:
                     public:
                       tables:
                         users:
                           allowed_columns: ["*"]
                           denied_columns: ["password_hash", "ssn"]
                   """;

        var ruleSet = DeserializeYaml(yaml);
        var usersTable = ruleSet.ExposedSchemas["public"].Tables["users"];

        Assert.Equal(new[] { "*" }, usersTable.AllowedColumns);
        Assert.Equal(new[] { "password_hash", "ssn" }, usersTable.DeniedColumns);
    }

    [Fact]
    public void Deserialize_NoDeniedColumns_DefaultsToNull()
    {
        var yaml = """
                   version: "1.0"
                   database: "test"
                   global_limits:
                     max_rows_returned: 100
                     timeout_ms: 1000
                     allowed_operations: [select, insert, update, delete]
                   exposed_schemas:
                     public:
                       tables:
                         logs:
                           allowed_columns: ["id", "message"]
                   """;

        var ruleSet = DeserializeYaml(yaml);

        Assert.Null(ruleSet.ExposedSchemas["public"].Tables["logs"].DeniedColumns);
        Assert.Equal(["select", "insert", "update", "delete"], ruleSet.GlobalLimits.AllowedOperations);
    }

    [Fact]
    public void Deserialize_AllowedOperations_AtTableLevel()
    {
        var yaml = """
                   version: "1.0"
                   database: "test"
                   global_limits:
                     max_rows_returned: 100
                     timeout_ms: 1000
                     allowed_operations: [select, insert, update, delete]
                   exposed_schemas:
                     public:
                       tables:
                         products:
                           allowed_columns: ["*"]
                           allowed_operations: [select]
                         orders:
                           allowed_columns: ["*"]
                   """;

        var ruleSet = DeserializeYaml(yaml);

        Assert.Equal(["select"], ruleSet.ExposedSchemas["public"].Tables["products"].AllowedOperations!);
        Assert.Null(ruleSet.ExposedSchemas["public"].Tables["orders"].AllowedOperations);
    }

    [Fact]
    public void Deserialize_AllowedOperations_DefaultsToSelectOnly()
    {
        var yaml = """
                   version: "1.0"
                   database: "test"
                   global_limits:
                     max_rows_returned: 100
                     timeout_ms: 1000
                   exposed_schemas:
                     public:
                       tables:
                         logs:
                           allowed_columns: ["id"]
                   """;

        var ruleSet = DeserializeYaml(yaml);

        Assert.Equal(["select"], ruleSet.GlobalLimits.AllowedOperations);
    }

    [Fact]
    public void IsOperationAllowed_GlobalLevel_RespectsAllowedList()
    {
        var limits = new GlobalLimits
        {
            MaxRowsReturned = 100,
            TimeoutMs = 1000,
            AllowedOperations = ["select", "insert"],
        };

        Assert.True(limits.IsOperationAllowed(StatementKind.Select));
        Assert.True(limits.IsOperationAllowed(StatementKind.Insert));
        Assert.False(limits.IsOperationAllowed(StatementKind.Update));
        Assert.False(limits.IsOperationAllowed(StatementKind.Delete));
        Assert.False(limits.IsOperationAllowed(StatementKind.Other));
    }

    [Fact]
    public void IsOperationAllowed_TableLevel_OverridesGlobal()
    {
        var limits = new GlobalLimits
        {
            MaxRowsReturned = 100,
            TimeoutMs = 1000,
            AllowedOperations = ["select", "insert", "update", "delete"],
        };
        var table = new TableRule
        {
            AllowedColumns = ["*"],
            AllowedOperations = ["select"],
        };

        Assert.True(table.IsOperationAllowed(StatementKind.Select, limits));
        Assert.False(table.IsOperationAllowed(StatementKind.Insert, limits));
    }

    [Fact]
    public void IsOperationAllowed_TableLevel_InheritsGlobal_WhenNull()
    {
        var limits = new GlobalLimits
        {
            MaxRowsReturned = 100,
            TimeoutMs = 1000,
            AllowedOperations = ["select", "insert"],
        };
        var table = new TableRule
        {
            AllowedColumns = ["*"],
        };

        Assert.True(table.IsOperationAllowed(StatementKind.Select, limits));
        Assert.True(table.IsOperationAllowed(StatementKind.Insert, limits));
        Assert.False(table.IsOperationAllowed(StatementKind.Update, limits));
    }
}