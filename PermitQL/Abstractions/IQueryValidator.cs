namespace PermitQL.Abstractions;

using Models;

public interface IQueryValidator
{
    ValueTask<ValidationResult> ValidateAsync(ParsedQuery query, RuleSet rules, CancellationToken cancellationToken = default);
}