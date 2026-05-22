namespace PermitQL.Tests.Rewriting;

using PermitQL.Abstractions;
using PermitQL.Models;
using PermitQL.Parsing;
using PermitQL.Rewriting.Dialects;
using NSubstitute;

public class TopQueryRewriterTests
{
    private readonly SqlAstProvider _astProvider = new();
    private readonly IDataAccessor _dataAccessor = Substitute.For<IDataAccessor>();

    private TopQueryRewriter CreateRewriter() => new(_dataAccessor);

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
            ExposedSchemas = schemas ?? new Dictionary<string, SchemaRule>
            {
                ["dbo"] = new SchemaRule
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        ["products"] = new TableRule
                        {
                            AllowedColumns = ["id", "name", "price"],
                            RowFilter = "is_active = 1",
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
    public async Task InjectsTop_WhenNoTopOrLimitPresent()
    {
        var parsed = _astProvider.GetOrParse("SELECT id FROM products");
        var rewriter = CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules(maxRows: 50));
        Assert.Contains("TOP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LowersTop_WhenExistingTopExceedsMax()
    {
        var parsed = _astProvider.GetOrParse("SELECT TOP 500 id FROM products");
        var rewriter = CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules(maxRows: 100));
        Assert.Contains("TOP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("500", sql);
    }

    [Fact]
    public async Task KeepsTop_WhenExistingTopBelowMax()
    {
        var parsed = _astProvider.GetOrParse("SELECT TOP 10 id FROM products");
        var rewriter = CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules(maxRows: 100));
        Assert.Contains("TOP 10", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConvertsLimitToTop()
    {
        var parsed = _astProvider.GetOrParse("SELECT id FROM products LIMIT 500");
        var rewriter = CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules(maxRows: 100));
        Assert.Contains("TOP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InjectsRowFilter_WhenNoWhereClause()
    {
        var parsed = _astProvider.GetOrParse("SELECT id FROM products");
        var rewriter = CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InjectsRowFilter_AndsWithExistingWhere()
    {
        var parsed = _astProvider.GetOrParse("SELECT id FROM products WHERE price > 10");
        var rewriter = CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("price > 10", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpandsSelectStar_WithExplicitAllowedColumns()
    {
        var parsed = _astProvider.GetOrParse("SELECT * FROM products");
        var rewriter = CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.DoesNotContain("*", sql);
        Assert.Contains("id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("price", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RowFilter_OnJoinedTable_InjectsIntoOnClause()
    {
        var parsed = _astProvider.GetOrParse(
            "SELECT o.id, p.name FROM orders o JOIN products p ON o.id = p.id");
        var rewriter = CreateRewriter();
        var sql = await rewriter.RewriteAsync(parsed, MakeRules());
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }
}