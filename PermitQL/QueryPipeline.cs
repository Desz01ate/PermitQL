namespace PermitQL;

using Abstractions;
using Exceptions;
using Models;

public sealed class QueryPipeline : IQueryPipeline
{
    private readonly IRulesProvider _rulesProvider;
    private readonly ISqlAstProvider _astProvider;
    private readonly IQueryValidator _validator;
    private readonly IQueryRewriter _rewriter;
    private readonly IDataAccessor _dataAccessor;

    public QueryPipeline(
        IRulesProvider rulesProvider,
        ISqlAstProvider astProvider,
        IQueryValidator validator,
        IQueryRewriter rewriter,
        IDataAccessor dataAccessor)
    {
        _rulesProvider = rulesProvider;
        _astProvider = astProvider;
        _validator = validator;
        _rewriter = rewriter;
        _dataAccessor = dataAccessor;
    }

    public async Task<QueryResult> ExecuteAsync(
        string query,
        string ruleSetKey,
        CancellationToken cancellationToken = default)
    {
        var rules = _rulesProvider.GetRuleSet(ruleSetKey);
        var parsed = _astProvider.GetOrParse(query);
        var validation = await _validator.ValidateAsync(parsed, rules, cancellationToken);
        if (validation.Type is ValidationResultType.Invalid)
            throw new QueryValidationFailedException(validation.Message ?? "Query validation failed.");

        using var timeoutCts = new CancellationTokenSource(rules.GlobalLimits.TimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var ct = linkedCts.Token;

        var rewrittenSql = await _rewriter.RewriteAsync(parsed, rules, ct);
        var columns = await _dataAccessor.GetColumnDefinitionAsync(rewrittenSql, ct);
        var rows = new List<object?[]>();
        await foreach (var row in _dataAccessor.QueryAsync(rewrittenSql, ct))
            rows.Add(row);
        return new QueryResult(columns, rows);
    }
}