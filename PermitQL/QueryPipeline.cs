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
        this._rulesProvider = rulesProvider;
        this._astProvider = astProvider;
        this._validator = validator;
        this._rewriter = rewriter;
        this._dataAccessor = dataAccessor;
    }

    public async Task<Result<QueryResult, Exception>> ExecuteAsync(
        string query,
        string ruleSetKey,
        CancellationToken cancellationToken = default)
    {
        RuleSet? rules = null;
        try
        {
            rules = this._rulesProvider.GetRuleSet(ruleSetKey);
            var parsed = this._astProvider.GetOrParse(query);
            var validation = await this._validator.ValidateAsync(parsed, rules, cancellationToken);

            if (validation.Type is ValidationResultType.Invalid)
            {
                throw new QueryValidationFailedException(validation.Message ?? "Query validation failed.");
            }

            using var timeoutCts = new CancellationTokenSource(rules.GlobalLimits.TimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var ct = linkedCts.Token;

            var rewrittenSql = await this._rewriter.RewriteAsync(parsed, rules, ct);

            var columns = await this._dataAccessor.GetColumnDefinitionAsync(rewrittenSql, ct);
            var rows = new List<object?[]>();

            await foreach (var row in this._dataAccessor.QueryAsync(rewrittenSql, ct))
            {
                rows.Add(row);
            }

            return new QueryResult(columns, rows);
        }
        catch (Exception e)
        {
            if (rules is not null && !rules.ExposeDetailedErrors
                && e is not QueryValidationFailedException
                && e is not AmbiguousTableException
                && e is not SqlParseException
                && e is not OperationCanceledException)
            {
                return new Exception("Query execution failed.");
            }

            return e;
        }
    }
}