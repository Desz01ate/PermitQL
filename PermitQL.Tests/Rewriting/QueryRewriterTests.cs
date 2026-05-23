namespace PermitQL.Tests.Rewriting;

using PermitQL.Abstractions;
using PermitQL.Models;
using PermitQL.Parsing;
using PermitQL.Rewriting.Dialects;
using NSubstitute;

public class QueryRewriterTests
{
    private readonly SqlAstProvider _astProvider = new();
    private readonly IDataAccessor _dataAccessor = Substitute.For<IDataAccessor>();

    private LimitQueryRewriter CreateRewriter() => new(this._dataAccessor);

    private static RuleSet MakeRules(
        int maxRows = 100,
        Dictionary<string, SchemaRule>? schemas = null)
    {
        return new RuleSet
        {
            Version = "1.0",
            Database = "test",
            GlobalLimits = new GlobalLimits
            {
                MaxRowsReturned = maxRows,
                TimeoutMs = 1000,
                AllowedOperations = ["select"],
            },
            ExposeDetailedErrors = false,
            ExposedSchemas = schemas ?? new Dictionary<string, SchemaRule>
            {
                ["public"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["products"] = new TableRule
                        {
                            AllowedColumns = ["id", "name", "price"],
                            RowFilter = "is_active = true",
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
    public async Task InjectsRowFilter_WhenNoWhereClause()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM products");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InjectsRowFilter_AndsWithExistingWhere()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM products WHERE price > 10");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("price > 10", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InjectsLimit_WhenNoLimitPresent()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM products");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules(maxRows: 50));
        Assert.Contains("LIMIT 50", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LowersLimit_WhenExistingLimitExceedsMax()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM products LIMIT 500");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules(maxRows: 100));
        Assert.Contains("LIMIT 100", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT 500", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetOperation_AppliesRowFilterAndLimit()
    {
        var parsed = this._astProvider.GetOrParse(
            "SELECT id FROM products UNION ALL SELECT id FROM products");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules(maxRows: 50));
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT 50", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KeepsLimit_WhenExistingLimitBelowMax()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM products LIMIT 10");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules(maxRows: 100));
        Assert.Contains("LIMIT 10", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpandsSelectStar_WithExplicitAllowedColumns()
    {
        var parsed = this._astProvider.GetOrParse("SELECT * FROM products");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.DoesNotContain("*", sql);
        Assert.Contains("id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("price", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpandsSelectStar_WithWildcardRules_FetchesMetadata()
    {
        var wildcardRules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposeDetailedErrors = false,
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
        this._dataAccessor
            .GetColumnDefinitionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ColumnDefinition>
            {
                new(0, "id", "integer", false),
                new(1, "email", "text", false),
                new(2, "password_hash", "text", false),
            });
        var parsed = this._astProvider.GetOrParse("SELECT * FROM users");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, wildcardRules);
        Assert.DoesNotContain("*", sql);
        Assert.Contains("id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password_hash", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cte_AppliesRowFilterToInnerQuery()
    {
        var parsed = this._astProvider.GetOrParse(
            "WITH active_products AS (SELECT id FROM products) SELECT * FROM active_products");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CteWithWildcardRules_ExcludesDeniedColumns()
    {
        var wildcardRules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1000, AllowedOperations = ["select"] },
            ExposeDetailedErrors = false,
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
        this._dataAccessor
            .GetColumnDefinitionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ColumnDefinition>
            {
                new(0, "id", "integer", false),
                new(1, "email", "text", false),
                new(2, "password_hash", "text", false),
            });
        var parsed = this._astProvider.GetOrParse(
            "WITH visible_users AS (SELECT * FROM users) SELECT * FROM visible_users");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, wildcardRules);
        Assert.DoesNotContain("*", sql);
        Assert.Contains("id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password_hash", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoRowFilter_SkipsInjection()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM orders");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.DoesNotContain("is_active", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RowFilter_OnJoinedTable_InjectsIntoOnClause()
    {
        var parsed = this._astProvider.GetOrParse(
            "SELECT o.id, p.name FROM orders o JOIN products p ON o.id = p.id");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        // products row filter should be alias-qualified with "p" and in ON clause
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RowFilter_OnFromTable_InjectsIntoWhere()
    {
        var parsed = this._astProvider.GetOrParse("SELECT id FROM products");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PassesThroughDml_Insert_WhenMutationsAllowed()
    {
        var rules = MakeRules();
        var parsed = this._astProvider.GetOrParse(
            "INSERT INTO products (name, price) VALUES ('Test', 9.99)");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, rules);
        Assert.Contains("INSERT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PassesThroughDml_Update_WhenMutationsAllowed()
    {
        var rules = MakeRules();
        var parsed = this._astProvider.GetOrParse("UPDATE products SET price = 5.00 WHERE id = 1");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, rules);
        Assert.Contains("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PassesThroughDml_Delete_WhenMutationsAllowed()
    {
        var rules = MakeRules();
        var parsed = this._astProvider.GetOrParse("DELETE FROM products WHERE id = 1");
        var rewriter = this.CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, rules);
        Assert.Contains("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
