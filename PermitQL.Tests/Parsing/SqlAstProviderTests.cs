namespace PermitQL.Tests.Parsing;

using PermitQL.Exceptions;
using PermitQL.Models;
using PermitQL.Parsing;

public class SqlAstProviderTests
{
    private readonly SqlAstProvider _provider = new(maxCacheSize: 100);

    [Fact]
    public void GetOrParse_SimpleSelect_ExtractsTableAndColumns()
    {
        var result = this._provider.GetOrParse("SELECT id, name FROM products");
        Assert.Equal(StatementKind.Select, result.StatementType);
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "id");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "name");
    }

    [Fact]
    public void GetOrParse_QualifiedTableName_ExtractsSchema()
    {
        var result = this._provider.GetOrParse("SELECT id FROM public.products");
        Assert.Contains(result.ReferencedTables, t => t is { Schema: "public", Table: "products" });
    }

    [Fact]
    public void GetOrParse_JoinQuery_ExtractsAllTables()
    {
        var result = this._provider.GetOrParse(
            "SELECT o.id, oi.quantity FROM orders o JOIN order_items oi ON o.id = oi.order_id");
        Assert.Contains(result.ReferencedTables, t => t.Table == "orders");
        Assert.Contains(result.ReferencedTables, t => t.Table == "order_items");
    }

    [Fact]
    public void GetOrParse_SelectStar_HasNoExplicitColumns()
    {
        var result = this._provider.GetOrParse("SELECT * FROM products");
        Assert.Equal(StatementKind.Select, result.StatementType);
        Assert.Empty(result.ReferencedColumns);
    }

    [Fact]
    public void GetOrParse_InsertStatement_DetectsStatementKind()
    {
        var result = this._provider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'test')");
        Assert.Equal(StatementKind.Insert, result.StatementType);
    }

    [Fact]
    public void GetOrParse_UpdateStatement_DetectsStatementKind()
    {
        var result = this._provider.GetOrParse("UPDATE products SET name = 'test' WHERE id = 1");
        Assert.Equal(StatementKind.Update, result.StatementType);
    }

    [Fact]
    public void GetOrParse_DeleteStatement_DetectsStatementKind()
    {
        var result = this._provider.GetOrParse("DELETE FROM products WHERE id = 1");
        Assert.Equal(StatementKind.Delete, result.StatementType);
    }

    [Fact]
    public void GetOrParse_CachesResult_ReturnsSameInstance()
    {
        var result1 = this._provider.GetOrParse("SELECT id FROM products");
        var result2 = this._provider.GetOrParse("SELECT id FROM products");
        Assert.Same(result1, result2);
    }

    [Fact]
    public void GetOrParse_InvalidSql_ThrowsWithMessage()
    {
        var ex = Assert.Throws<SqlParseException>(() => this._provider.GetOrParse("SELECTT GARBAGE !!!"));
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void GetOrParse_MultipleStatements_Throws()
    {
        Assert.Throws<SqlParseException>(
            () => this._provider.GetOrParse("SELECT * FROM products; DELETE FROM orders;"));
    }

    [Fact]
    public void GetOrParse_WhereClauseColumns_ExtractsReferencedColumns()
    {
        var result = this._provider.GetOrParse("SELECT id FROM products WHERE price > 100");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "id");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "price");
    }

    [Fact]
    public void GetOrParse_QualifiedColumns_ExtractsTableQualification()
    {
        var result = this._provider.GetOrParse("SELECT p.id, p.name FROM products p");
        Assert.Contains(result.ReferencedColumns, c => c is { Table: "p", Column: "id" });
        Assert.Contains(result.ReferencedColumns, c => c is { Table: "p", Column: "name" });
    }

    [Fact]
    public void GetOrParse_JoinOnColumns_ExtractsFromJoinPredicate()
    {
        var result = this._provider.GetOrParse(
            "SELECT o.id FROM orders o JOIN order_items oi ON o.id = oi.order_id");
        Assert.Contains(result.ReferencedColumns, c => c is { Table: "oi", Column: "order_id" });
    }

    [Fact]
    public void GetOrParse_OrderByColumns_Extracted()
    {
        var result = this._provider.GetOrParse("SELECT id FROM products ORDER BY price");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "price");
    }

    [Fact]
    public void GetOrParse_GroupByColumns_Extracted()
    {
        var result = this._provider.GetOrParse("SELECT COUNT(id) FROM orders GROUP BY customer_id");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "customer_id");
    }

    [Fact]
    public void GetOrParse_HavingColumns_Extracted()
    {
        var result = this._provider.GetOrParse(
            "SELECT customer_id FROM orders GROUP BY customer_id HAVING COUNT(total_amount) > 100");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "total_amount");
    }

    [Fact]
    public void GetOrParse_Insert_ExtractsMutationTarget()
    {
        var result = this._provider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'test')");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("products", result.MutationTarget!.Table);
        Assert.Null(result.MutationTarget.Schema);
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
    }

    [Fact]
    public void GetOrParse_Insert_SchemaQualified_ExtractsMutationTarget()
    {
        var result = this._provider.GetOrParse("INSERT INTO public.products (id) VALUES (1)");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("public", result.MutationTarget!.Schema);
        Assert.Equal("products", result.MutationTarget.Table);
    }

    [Fact]
    public void GetOrParse_Update_ExtractsMutationTarget()
    {
        var result = this._provider.GetOrParse("UPDATE products SET name = 'test' WHERE id = 1");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("products", result.MutationTarget!.Table);
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
    }

    [Fact]
    public void GetOrParse_Update_SchemaQualified_ExtractsMutationTarget()
    {
        var result = this._provider.GetOrParse("UPDATE public.products SET name = 'test' WHERE id = 1");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("public", result.MutationTarget!.Schema);
        Assert.Equal("products", result.MutationTarget.Table);
    }

    [Fact]
    public void GetOrParse_Delete_ExtractsMutationTarget()
    {
        var result = this._provider.GetOrParse("DELETE FROM products WHERE id = 1");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("products", result.MutationTarget!.Table);
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
    }

    [Fact]
    public void GetOrParse_Delete_SchemaQualified_ExtractsMutationTarget()
    {
        var result = this._provider.GetOrParse("DELETE FROM public.products WHERE id = 1");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("public", result.MutationTarget!.Schema);
        Assert.Equal("products", result.MutationTarget.Table);
    }

    [Fact]
    public void GetOrParse_Select_MutationTargetIsNull()
    {
        var result = this._provider.GetOrParse("SELECT id FROM products");
        Assert.Null(result.MutationTarget);
    }

    // --- CTE parsing tests ---

    [Fact]
    public void GetOrParse_SimpleCte_ExtractsRealTableExcludesCteName()
    {
        var result = this._provider.GetOrParse(
            "WITH active AS (SELECT id FROM products) SELECT * FROM active");
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
        Assert.DoesNotContain(result.ReferencedTables, t => t.Table == "active");
        Assert.NotNull(result.CteDefinitions);
        Assert.Single(result.CteDefinitions!);
        Assert.Equal("active", result.CteDefinitions![0].Name);
        Assert.Contains(result.CteDefinitions[0].InnerReferencedTables, t => t.Table == "products");
    }

    [Fact]
    public void GetOrParse_MultipleCtes_ExtractsBothCorrectly()
    {
        var result = this._provider.GetOrParse(
            "WITH a AS (SELECT id FROM products), b AS (SELECT id FROM orders) SELECT * FROM a JOIN b ON a.id = b.id");
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
        Assert.Contains(result.ReferencedTables, t => t.Table == "orders");
        Assert.DoesNotContain(result.ReferencedTables, t => t.Table == "a");
        Assert.DoesNotContain(result.ReferencedTables, t => t.Table == "b");
        Assert.Equal(2, result.CteDefinitions!.Count);
    }

    [Fact]
    public void GetOrParse_ChainedCtes_LaterReferencingEarlier_ExcludesEarlierFromInnerTables()
    {
        var result = this._provider.GetOrParse(
            "WITH a AS (SELECT id FROM products), b AS (SELECT id FROM a) SELECT * FROM b");
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
        Assert.DoesNotContain(result.ReferencedTables, t => t.Table == "a");
        Assert.DoesNotContain(result.ReferencedTables, t => t.Table == "b");
        Assert.DoesNotContain(result.CteDefinitions![1].InnerReferencedTables, t => t.Table == "a");
    }

    [Fact]
    public void GetOrParse_CteWithColumnAliases_ExtractsAliases()
    {
        var result = this._provider.GetOrParse(
            "WITH cte(product_id, product_name) AS (SELECT id, name FROM products) SELECT product_id FROM cte");
        Assert.NotNull(result.CteDefinitions![0].ColumnAliases);
        Assert.Equal(["product_id", "product_name"], result.CteDefinitions[0].ColumnAliases);
    }

    [Fact]
    public void GetOrParse_RecursiveCte_ExcludesSelfReference()
    {
        var result = this._provider.GetOrParse(
            "WITH RECURSIVE hierarchy AS (SELECT id, parent_id FROM categories WHERE parent_id IS NULL UNION ALL SELECT c.id, c.parent_id FROM categories c JOIN hierarchy h ON c.parent_id = h.id) SELECT * FROM hierarchy");
        Assert.Contains(result.ReferencedTables, t => t.Table == "categories");
        Assert.DoesNotContain(result.ReferencedTables, t => t.Table == "hierarchy");
    }

    [Fact]
    public void GetOrParse_CteWithJoin_ExtractsAllInnerTables()
    {
        var result = this._provider.GetOrParse(
            "WITH cte AS (SELECT p.id, o.total_amount FROM products p JOIN orders o ON p.id = o.customer_id) SELECT * FROM cte");
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
        Assert.Contains(result.ReferencedTables, t => t.Table == "orders");
        Assert.DoesNotContain(result.ReferencedTables, t => t.Table == "cte");
    }

    [Fact]
    public void GetOrParse_NoCte_CteDefinitionsIsNull()
    {
        var result = this._provider.GetOrParse("SELECT id FROM products");
        Assert.Null(result.CteDefinitions);
    }

    [Fact]
    public void GetOrParse_CteWithInnerAliases_BuildsAliasMap()
    {
        var result = this._provider.GetOrParse(
            "WITH cte AS (SELECT p.id FROM products p) SELECT * FROM cte");
        Assert.NotNull(result.CteDefinitions![0].InnerAliasMap);
        Assert.True(result.CteDefinitions[0].InnerAliasMap.ContainsKey("p"));
        Assert.Equal("products", result.CteDefinitions[0].InnerAliasMap["p"]);
    }
}
