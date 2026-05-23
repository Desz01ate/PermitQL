namespace PermitQL.Tests.Server;

using PermitQL.Abstractions;
using PermitQL.Models;
using PermitQL.Server.Tools;
using NSubstitute;

public sealed class QueryToolTests
{
    private readonly IQueryPipeline _pipeline = Substitute.For<IQueryPipeline>();

    [Fact]
    public async Task Markdown_StringArrayCell_FormatsAsCommaSeparatedList()
    {
        _pipeline.ExecuteAsync("SELECT tags FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "tags", "text[]", true)],
                [new object?[] { new[] { "a", "b", "c" } }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT tags FROM t", "k", "markdown");

        Assert.Contains("[a, b, c]", result);
        Assert.DoesNotContain("System.String[]", result);
    }

    [Fact]
    public async Task Markdown_EmptyArray_FormatsAsEmptyBrackets()
    {
        _pipeline.ExecuteAsync("SELECT tags FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "tags", "text[]", true)],
                [new object?[] { Array.Empty<string>() }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT tags FROM t", "k", "markdown");

        Assert.Contains("[]", result);
        Assert.DoesNotContain("System.String[]", result);
    }

    [Fact]
    public async Task Markdown_IntArrayCell_FormatsAsCommaSeparatedList()
    {
        _pipeline.ExecuteAsync("SELECT ids FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "ids", "int4[]", true)],
                [new object?[] { new[] { 1, 2, 3 } }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT ids FROM t", "k", "markdown");

        Assert.Contains("[1, 2, 3]", result);
        Assert.DoesNotContain("System.Int32[]", result);
    }

    [Fact]
    public async Task Markdown_NullCell_FormatsAsNULL()
    {
        _pipeline.ExecuteAsync("SELECT tags FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "tags", "text[]", true)],
                [new object?[] { null }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT tags FROM t", "k", "markdown");

        Assert.Contains("NULL", result);
    }

    [Fact]
    public async Task Markdown_ScalarStringCell_NotWrappedInBrackets()
    {
        _pipeline.ExecuteAsync("SELECT name FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "name", "text", false)],
                [new object?[] { "hello" }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT name FROM t", "k", "markdown");

        Assert.Contains("hello", result);
        Assert.DoesNotContain("[hello]", result);
    }

    [Fact]
    public async Task Json_StringArrayCell_SerializesCorrectly()
    {
        _pipeline.ExecuteAsync("SELECT tags FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "tags", "text[]", true)],
                [new object?[] { new[] { "x", "y" } }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT tags FROM t", "k", "json");

        Assert.DoesNotContain("System.String[]", result);
        Assert.Contains("\"x\"", result);
        Assert.Contains("\"y\"", result);
    }
}
