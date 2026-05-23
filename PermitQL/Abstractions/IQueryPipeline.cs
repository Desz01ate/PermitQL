namespace PermitQL.Abstractions;

using Models;

public interface IQueryPipeline
{
    Task<Result<QueryResult, Exception>> ExecuteAsync(string query, string ruleSetKey, CancellationToken cancellationToken = default);
}