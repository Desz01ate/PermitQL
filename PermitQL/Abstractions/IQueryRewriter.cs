namespace PermitQL.Abstractions;

using Models;

public interface IQueryRewriter
{
    ValueTask<string> RewriteAsync(ParsedQuery query, RuleSet rules, CancellationToken cancellationToken = default);
}