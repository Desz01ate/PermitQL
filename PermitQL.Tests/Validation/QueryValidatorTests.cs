namespace PermitQL.Tests.Validation;

using PermitQL.Models;
using PermitQL.Parsing;
using PermitQL.Validation;

public class QueryValidatorTests
{
    private readonly SqlAstProvider _astProvider = new();
    private readonly QueryValidator _validator = new();

    private static RuleSet MakeRules(
        string[]? allowedOperations = null,
        Dictionary<string, SchemaRule>? schemas = null)
    {
        return new RuleSet
        {
            Version = "1.0",
            Database = "test",
            GlobalLimits = new GlobalLimits
            {
                MaxRowsReturned = 100,
                TimeoutMs = 1000,
                AllowedOperations = allowedOperations ?? ["select"],
            },
            ExposedSchemas = schemas ?? new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["products"] = new TableRule
                        {
                            AllowedColumns = ["id", "name", "price"],
                        },
                        ["orders"] = new TableRule
                        {
                            AllowedColumns = ["id", "customer_id", "total_amount"],
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public async Task ValidSelect_ReturnsValid()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id, name FROM products");
        var result = await this._validator.ValidateAsync(parsed, MakeRules());
        Assert.Equal(ValidationResultType.Valid, result.Type);
    }

    [Fact]
    public async Task InsertWithMutationsDisallowed_ReturnsInvalid()
    {
        var parsed = this._astProvider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'x')");
        var result = await this._validator.ValidateAsync(parsed, MakeRules(allowedOperations: ["select"]));
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("not allowed", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownTable_ReturnsInvalid()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM secret_table");
        var result = await this._validator.ValidateAsync(parsed, MakeRules());
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("secret_table", result.Message!);
    }

    [Fact]
    public async Task DisallowedColumn_ReturnsInvalid()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id, secret_field FROM products");
        var result = await this._validator.ValidateAsync(parsed, MakeRules());
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("secret_field", result.Message!);
    }

    [Fact]
    public async Task SelectStar_IsAllowed()
    {
        var parsed = this._astProvider.GetOrParse("SELECT * FROM products");
        var result = await this._validator.ValidateAsync(parsed, MakeRules());
        Assert.Equal(ValidationResultType.Valid, result.Type);
    }

    [Fact]
    public async Task JoinWithAllowedTables_ReturnsValid()
    {
        var parsed = this._astProvider.GetOrParse(
            "SELECT p.id, o.total_amount FROM products p JOIN orders o ON p.id = o.customer_id");
        var result = await this._validator.ValidateAsync(parsed, MakeRules());
        Assert.Equal(ValidationResultType.Valid, result.Type);
    }

    [Fact]
    public async Task JoinWithUnknownTable_ReturnsInvalid()
    {
        var parsed = this._astProvider.GetOrParse(
            "SELECT p.id FROM products p JOIN secrets s ON p.id = s.product_id");
        var result = await this._validator.ValidateAsync(parsed, MakeRules());
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("secrets", result.Message!);
    }

    [Fact]
    public async Task WildcardColumns_DeniedColumnInSelect_ReturnsInvalid()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["users"] = new TableRule { AllowedColumns = ["*"], DeniedColumns = ["password_hash"] },
                    },
                },
            },
        };
        var parsed = this._astProvider.GetOrParse("SELECT password_hash FROM users");
        var result = await this._validator.ValidateAsync(parsed, rules);
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("password_hash", result.Message!);
    }

    [Fact]
    public async Task WildcardColumns_AllowedColumn_ReturnsValid()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["users"] = new TableRule { AllowedColumns = ["*"], DeniedColumns = ["password_hash"] },
                    },
                },
            },
        };
        var parsed = this._astProvider.GetOrParse("SELECT id, email FROM users");
        var result = await this._validator.ValidateAsync(parsed, rules);
        Assert.Equal(ValidationResultType.Valid, result.Type);
    }

    [Fact]
    public async Task SchemaQualifiedTable_MatchesRules()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM public.products");
        var result = await this._validator.ValidateAsync(parsed, MakeRules());
        Assert.Equal(ValidationResultType.Valid, result.Type);
    }

    [Fact]
    public async Task WhereClauseWithDisallowedColumn_ReturnsInvalid()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM products WHERE secret_field = 'x'");
        var result = await this._validator.ValidateAsync(parsed, MakeRules());
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("secret_field", result.Message!);
    }

    [Fact]
    public async Task AmbiguousTable_AcrossMultipleSchemas_ThrowsAmbiguousTableException()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["users"] = new TableRule { AllowedColumns = ["id", "name"] },
                    },
                },
                ["tenant"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["users"] = new TableRule { AllowedColumns = ["id", "email"] },
                    },
                },
            },
        };
        var parsed = this._astProvider.GetOrParse("SELECT id FROM users");
        await Assert.ThrowsAsync<PermitQL.Exceptions.AmbiguousTableException>(
            () => this._validator.ValidateAsync(parsed, rules).AsTask());
    }

    [Fact]
    public async Task JoinOnColumn_WithAllowedTables_ExtractsAndValidatesJoinColumns()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["orders"] = new TableRule { AllowedColumns = ["id", "customer_id", "total_amount"] },
                        ["order_items"] = new TableRule { AllowedColumns = ["order_id", "product_id", "quantity"] },
                    },
                },
            },
        };
        var parsed = this._astProvider.GetOrParse(
            "SELECT o.id FROM orders o JOIN order_items oi ON o.id = oi.order_id ORDER BY o.total_amount");
        var result = await this._validator.ValidateAsync(parsed, rules);
        Assert.Equal(ValidationResultType.Valid, result.Type);
    }

    [Fact]
    public async Task JoinOnColumn_WithDisallowedColumn_ReturnsInvalid()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["orders"] = new TableRule { AllowedColumns = ["id"] },
                        ["order_items"] = new TableRule { AllowedColumns = ["order_id"] },
                    },
                },
            },
        };
        var parsed = this._astProvider.GetOrParse(
            "SELECT o.id FROM orders o JOIN order_items oi ON o.id = oi.order_id ORDER BY o.total_amount");
        var result = await this._validator.ValidateAsync(parsed, rules);
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("total_amount", result.Message!);
    }

    // --- Per-table mutation permission tests ---

    [Fact]
    public async Task InsertAllowedByGlobal_ReturnsValid()
    {
        var parsed = this._astProvider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'x')");
        var result = await this._validator.ValidateAsync(parsed, MakeRules(allowedOperations: ["select", "insert"]));
        Assert.Equal(ValidationResultType.Valid, result.Type);
    }

    [Fact]
    public async Task UpdateDeniedByGlobal_ReturnsInvalid()
    {
        var parsed = this._astProvider.GetOrParse("UPDATE products SET name = 'y' WHERE id = 1");
        var result = await this._validator.ValidateAsync(parsed, MakeRules(allowedOperations: ["select", "insert"]));
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("Update", result.Message!);
        Assert.Contains("not allowed", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteDeniedByGlobal_ReturnsInvalid()
    {
        var parsed = this._astProvider.GetOrParse("DELETE FROM products WHERE id = 1");
        var result = await this._validator.ValidateAsync(parsed, MakeRules(allowedOperations: ["select"]));
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("Delete", result.Message!);
    }

    [Fact]
    public async Task PerTableOverride_DeniesInsert_WhenGlobalAllows()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select", "insert", "update", "delete"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["products"] = new TableRule
                        {
                            AllowedColumns = ["id", "name", "price"],
                            AllowedOperations = ["select"],
                        },
                    },
                },
            },
        };
        var parsed = this._astProvider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'x')");
        var result = await this._validator.ValidateAsync(parsed, rules);
        Assert.Equal(ValidationResultType.Invalid, result.Type);
        Assert.Contains("Insert", result.Message!);
        Assert.Contains("products", result.Message!);
    }

    [Fact]
    public async Task PerTableOverride_AllowsInsert_WhenGlobalDenies()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["products"] = new TableRule
                        {
                            AllowedColumns = ["id", "name", "price"],
                            AllowedOperations = ["select", "insert"],
                        },
                    },
                },
            },
        };
        var parsed = this._astProvider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'x')");
        var result = await this._validator.ValidateAsync(parsed, rules);
        Assert.Equal(ValidationResultType.Valid, result.Type);
    }

    [Fact]
    public async Task PerTableOverride_AppendOnly_AllowsInsertDeniesUpdate()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select", "insert", "update", "delete"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["audit_log"] = new TableRule
                        {
                            AllowedColumns = ["*"],
                            AllowedOperations = ["select", "insert"],
                        },
                    },
                },
            },
        };

        var insertParsed = this._astProvider.GetOrParse("INSERT INTO audit_log (id) VALUES (1)");
        var insertResult = await this._validator.ValidateAsync(insertParsed, rules);
        Assert.Equal(ValidationResultType.Valid, insertResult.Type);

        var updateParsed = this._astProvider.GetOrParse("UPDATE audit_log SET id = 2 WHERE id = 1");
        var updateResult = await this._validator.ValidateAsync(updateParsed, rules);
        Assert.Equal(ValidationResultType.Invalid, updateResult.Type);

        var deleteParsed = this._astProvider.GetOrParse("DELETE FROM audit_log WHERE id = 1");
        var deleteResult = await this._validator.ValidateAsync(deleteParsed, rules);
        Assert.Equal(ValidationResultType.Invalid, deleteResult.Type);
    }

    [Fact]
    public async Task MixedTables_ReadOnlyAndWritable()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["products"] = new TableRule
                        {
                            AllowedColumns = ["id", "name"],
                        },
                        ["orders"] = new TableRule
                        {
                            AllowedColumns = ["id", "customer_id"],
                            AllowedOperations = ["select", "insert", "update", "delete"],
                        },
                    },
                },
            },
        };

        var insertProducts = this._astProvider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'x')");
        var result1 = await this._validator.ValidateAsync(insertProducts, rules);
        Assert.Equal(ValidationResultType.Invalid, result1.Type);

        var insertOrders = this._astProvider.GetOrParse("INSERT INTO orders (id, customer_id) VALUES (1, 2)");
        var result2 = await this._validator.ValidateAsync(insertOrders, rules);
        Assert.Equal(ValidationResultType.Valid, result2.Type);
    }

    [Fact]
    public async Task TableInheritsGlobal_WhenNoTableOverride()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select", "insert"] },
            ExposedSchemas = new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["products"] = new TableRule { AllowedColumns = ["id", "name"] },
                    },
                },
            },
        };

        var insertParsed = this._astProvider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'x')");
        var insertResult = await this._validator.ValidateAsync(insertParsed, rules);
        Assert.Equal(ValidationResultType.Valid, insertResult.Type);

        var deleteParsed = this._astProvider.GetOrParse("DELETE FROM products WHERE id = 1");
        var deleteResult = await this._validator.ValidateAsync(deleteParsed, rules);
        Assert.Equal(ValidationResultType.Invalid, deleteResult.Type);
    }
}