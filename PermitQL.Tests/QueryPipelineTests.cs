namespace PermitQL.Tests;

using PermitQL.Abstractions;
using PermitQL.Exceptions;
using PermitQL.Models;
using NSubstitute;

public class QueryPipelineTests
{
    private readonly IRulesProvider _rulesProvider = Substitute.For<IRulesProvider>();
    private readonly ISqlAstProvider _astProvider = Substitute.For<ISqlAstProvider>();
    private readonly IQueryValidator _validator = Substitute.For<IQueryValidator>();
    private readonly IQueryRewriter _rewriter = Substitute.For<IQueryRewriter>();
    private readonly IDataAccessor _dataAccessor = Substitute.For<IDataAccessor>();

    private QueryPipeline CreatePipeline() => new(this._rulesProvider, this._astProvider, this._validator, this._rewriter, this._dataAccessor);

    private static RuleSet MakeRules() => new()
    {
        Version = "1.0", Database = "test",
        GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 5000, AllowedOperations = ["select"] },
        ExposeDetailedErrors = false,
        ExposedSchemas = new Dictionary<string, SchemaRule>(),
    };

    private static ParsedQuery MakeParsedQuery()
    {
        var parser = new PermitQL.Parsing.SqlAstProvider();
        return parser.GetOrParse("SELECT 1");
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_ReturnsQueryResult()
    {
        var rules = MakeRules();
        var parsed = MakeParsedQuery();
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._astProvider.GetOrParse("SELECT 1").Returns(parsed);
        this._validator.ValidateAsync(parsed, rules, Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(ValidationResultType.Valid, null));
        this._rewriter.RewriteAsync(parsed, rules, Arg.Any<CancellationToken>())
            .Returns("SELECT 1 LIMIT 100");
        this._dataAccessor.GetColumnDefinitionAsync("SELECT 1 LIMIT 100", Arg.Any<CancellationToken>())
            .Returns(new List<ColumnDefinition> { new(0, "col", "integer", false) });
        this._dataAccessor.QueryAsync("SELECT 1 LIMIT 100", Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(new object?[] { 1 }));
        var pipeline = this.CreatePipeline();
        var result = await pipeline.ExecuteAsync("SELECT 1", "test");
        Assert.NotNull(result.Success);
        Assert.Single(result.Success.Columns);
        Assert.Single(result.Success.Rows);
    }

    [Fact]
    public async Task ExecuteAsync_ValidationFails_ThrowsQueryValidationFailedException()
    {
        var rules = MakeRules();
        var parsed = MakeParsedQuery();
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._astProvider.GetOrParse("SELECT 1").Returns(parsed);
        this._validator.ValidateAsync(parsed, rules, Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(ValidationResultType.Invalid, "not allowed"));
        var pipeline = this.CreatePipeline();
        var result = await pipeline.ExecuteAsync("SELECT 1", "test");
        Assert.Null(result.Success);
        Assert.NotNull(result.Error);
        Assert.IsType<QueryValidationFailedException>(result.Error);
        Assert.Contains("not allowed", result.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ThrowsTaskCanceledException()
    {
        var rules = new RuleSet
        {
            Version = "1.0", Database = "test",
            GlobalLimits = new GlobalLimits { MaxRowsReturned = 100, TimeoutMs = 1, AllowedOperations = ["select"] },
            ExposeDetailedErrors = false,
            ExposedSchemas = new Dictionary<string, SchemaRule>(),
        };
        var parsed = MakeParsedQuery();
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._astProvider.GetOrParse("SELECT 1").Returns(parsed);
        this._validator.ValidateAsync(parsed, rules, Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(ValidationResultType.Valid, null));
        this._rewriter.RewriteAsync(parsed, rules, Arg.Any<CancellationToken>())
            .Returns("SELECT 1");
        this._dataAccessor.GetColumnDefinitionAsync("SELECT 1", Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<IReadOnlyList<ColumnDefinition>>(
                Task.Run(async () =>
                {
                    await Task.Delay(5000, call.Arg<CancellationToken>());
                    return (IReadOnlyList<ColumnDefinition>)new List<ColumnDefinition>();
                }, call.Arg<CancellationToken>())));
        var pipeline = this.CreatePipeline();
        var result = await pipeline.ExecuteAsync("SELECT 1", "test");
        Assert.Null(result.Success);
        Assert.NotNull(result.Error);
        Assert.IsType<TaskCanceledException>(result.Error);
    }

    private static async IAsyncEnumerable<object?[]> ToAsyncEnumerable(params object?[][] rows)
    {
        foreach (var row in rows)
        {
            await Task.CompletedTask;
            yield return row;
        }
    }
}