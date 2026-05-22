namespace PermitQL.Abstractions;

using Models;

public interface IQueryPipeline
{
    Task<QueryResult> ExecuteAsync(string query, string ruleSetKey, CancellationToken cancellationToken = default);
}