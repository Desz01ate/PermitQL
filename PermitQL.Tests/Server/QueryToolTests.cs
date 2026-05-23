namespace PermitQL.Tests.Server;

using System.Collections;
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

    [Fact]
    public async Task Markdown_ByteArrayCell_FormatsAsBase64()
    {
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        _pipeline.ExecuteAsync("SELECT data FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "data", "bytea", false)],
                [new object?[] { bytes }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT data FROM t", "k", "markdown");

        Assert.Contains(Convert.ToBase64String(bytes), result);
        Assert.DoesNotContain("[72,", result);
    }

    [Fact]
    public async Task Markdown_BitArrayCell_FormatsAsBitString()
    {
        var bits = new BitArray(new[] { true, false, true, true, false });
        _pipeline.ExecuteAsync("SELECT flags FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "flags", "bit", false)],
                [new object?[] { bits }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT flags FROM t", "k", "markdown");

        Assert.Contains("10110", result);
        Assert.DoesNotContain("True", result);
    }

    [Fact]
    public async Task Markdown_DictionaryCell_FormatsAsJson()
    {
        var dict = new Dictionary<string, string> { ["key1"] = "val1", ["key2"] = "val2" };
        _pipeline.ExecuteAsync("SELECT meta FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "meta", "hstore", false)],
                [new object?[] { dict }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT meta FROM t", "k", "markdown");

        Assert.Contains("key1", result);
        Assert.Contains("val1", result);
        Assert.DoesNotContain("KeyValuePair", result);
        Assert.DoesNotContain("[key1", result);
    }

    [Fact]
    public async Task Markdown_GuidCell_FormatsWithoutQuotes()
    {
        var guid = Guid.Parse("a74a28cf-04df-4fc4-9d8c-b333a9b74022");
        _pipeline.ExecuteAsync("SELECT id FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "id", "uuid", false)],
                [new object?[] { guid }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT id FROM t", "k", "markdown");

        Assert.Contains("a74a28cf-04df-4fc4-9d8c-b333a9b74022", result);
        Assert.DoesNotContain("\"a74a28cf", result);
    }

    [Fact]
    public async Task Markdown_DateTimeOffsetCell_FormatsAsInvariantString()
    {
        var dto = new DateTimeOffset(2026, 5, 23, 10, 30, 0, TimeSpan.FromHours(7));
        _pipeline.ExecuteAsync("SELECT ts FROM t", "k", Arg.Any<CancellationToken>())
            .Returns(new QueryResult(
                [new(0, "ts", "timestamp with time zone", false)],
                [new object?[] { dto }]));

        var result = await PermitQLTools.Query(_pipeline, "SELECT ts FROM t", "k", "markdown");

        Assert.Contains("05/23/2026", result);
        Assert.DoesNotContain("{", result);
    }
}
