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
        var result = _provider.GetOrParse("SELECT id, name FROM products");
        Assert.Equal(StatementKind.Select, result.StatementType);
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "id");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "name");
    }

    [Fact]
    public void GetOrParse_QualifiedTableName_ExtractsSchema()
    {
        var result = _provider.GetOrParse("SELECT id FROM public.products");
        Assert.Contains(result.ReferencedTables, t => t is { Schema: "public", Table: "products" });
    }

    [Fact]
    public void GetOrParse_JoinQuery_ExtractsAllTables()
    {
        var result = _provider.GetOrParse(
            "SELECT o.id, oi.quantity FROM orders o JOIN order_items oi ON o.id = oi.order_id");
        Assert.Contains(result.ReferencedTables, t => t.Table == "orders");
        Assert.Contains(result.ReferencedTables, t => t.Table == "order_items");
    }

    [Fact]
    public void GetOrParse_SelectStar_HasNoExplicitColumns()
    {
        var result = _provider.GetOrParse("SELECT * FROM products");
        Assert.Equal(StatementKind.Select, result.StatementType);
        Assert.Empty(result.ReferencedColumns);
    }

    [Fact]
    public void GetOrParse_InsertStatement_DetectsStatementKind()
    {
        var result = _provider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'test')");
        Assert.Equal(StatementKind.Insert, result.StatementType);
    }

    [Fact]
    public void GetOrParse_UpdateStatement_DetectsStatementKind()
    {
        var result = _provider.GetOrParse("UPDATE products SET name = 'test' WHERE id = 1");
        Assert.Equal(StatementKind.Update, result.StatementType);
    }

    [Fact]
    public void GetOrParse_DeleteStatement_DetectsStatementKind()
    {
        var result = _provider.GetOrParse("DELETE FROM products WHERE id = 1");
        Assert.Equal(StatementKind.Delete, result.StatementType);
    }

    [Fact]
    public void GetOrParse_CachesResult_ReturnsSameInstance()
    {
        var result1 = _provider.GetOrParse("SELECT id FROM products");
        var result2 = _provider.GetOrParse("SELECT id FROM products");
        Assert.Same(result1, result2);
    }

    [Fact]
    public void GetOrParse_InvalidSql_ThrowsWithMessage()
    {
        var ex = Assert.Throws<SqlParseException>(() => _provider.GetOrParse("SELECTT GARBAGE !!!"));
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void GetOrParse_WhereClauseColumns_ExtractsReferencedColumns()
    {
        var result = _provider.GetOrParse("SELECT id FROM products WHERE price > 100");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "id");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "price");
    }

    [Fact]
    public void GetOrParse_QualifiedColumns_ExtractsTableQualification()
    {
        var result = _provider.GetOrParse("SELECT p.id, p.name FROM products p");
        Assert.Contains(result.ReferencedColumns, c => c is { Table: "p", Column: "id" });
        Assert.Contains(result.ReferencedColumns, c => c is { Table: "p", Column: "name" });
    }

    [Fact]
    public void GetOrParse_JoinOnColumns_ExtractsFromJoinPredicate()
    {
        var result = _provider.GetOrParse(
            "SELECT o.id FROM orders o JOIN order_items oi ON o.id = oi.order_id");
        Assert.Contains(result.ReferencedColumns, c => c is { Table: "oi", Column: "order_id" });
    }

    [Fact]
    public void GetOrParse_OrderByColumns_Extracted()
    {
        var result = _provider.GetOrParse("SELECT id FROM products ORDER BY price");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "price");
    }

    [Fact]
    public void GetOrParse_GroupByColumns_Extracted()
    {
        var result = _provider.GetOrParse("SELECT COUNT(id) FROM orders GROUP BY customer_id");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "customer_id");
    }

    [Fact]
    public void GetOrParse_HavingColumns_Extracted()
    {
        var result = _provider.GetOrParse(
            "SELECT customer_id FROM orders GROUP BY customer_id HAVING COUNT(total_amount) > 100");
        Assert.Contains(result.ReferencedColumns, c => c.Column == "total_amount");
    }

    [Fact]
    public void GetOrParse_Insert_ExtractsMutationTarget()
    {
        var result = _provider.GetOrParse("INSERT INTO products (id, name) VALUES (1, 'test')");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("products", result.MutationTarget!.Table);
        Assert.Null(result.MutationTarget.Schema);
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
    }

    [Fact]
    public void GetOrParse_Insert_SchemaQualified_ExtractsMutationTarget()
    {
        var result = _provider.GetOrParse("INSERT INTO public.products (id) VALUES (1)");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("public", result.MutationTarget!.Schema);
        Assert.Equal("products", result.MutationTarget.Table);
    }

    [Fact]
    public void GetOrParse_Update_ExtractsMutationTarget()
    {
        var result = _provider.GetOrParse("UPDATE products SET name = 'test' WHERE id = 1");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("products", result.MutationTarget!.Table);
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
    }

    [Fact]
    public void GetOrParse_Update_SchemaQualified_ExtractsMutationTarget()
    {
        var result = _provider.GetOrParse("UPDATE public.products SET name = 'test' WHERE id = 1");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("public", result.MutationTarget!.Schema);
        Assert.Equal("products", result.MutationTarget.Table);
    }

    [Fact]
    public void GetOrParse_Delete_ExtractsMutationTarget()
    {
        var result = _provider.GetOrParse("DELETE FROM products WHERE id = 1");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("products", result.MutationTarget!.Table);
        Assert.Contains(result.ReferencedTables, t => t.Table == "products");
    }

    [Fact]
    public void GetOrParse_Delete_SchemaQualified_ExtractsMutationTarget()
    {
        var result = _provider.GetOrParse("DELETE FROM public.products WHERE id = 1");
        Assert.NotNull(result.MutationTarget);
        Assert.Equal("public", result.MutationTarget!.Schema);
        Assert.Equal("products", result.MutationTarget.Table);
    }

    [Fact]
    public void GetOrParse_Select_MutationTargetIsNull()
    {
        var result = _provider.GetOrParse("SELECT id FROM products");
        Assert.Null(result.MutationTarget);
    }
}