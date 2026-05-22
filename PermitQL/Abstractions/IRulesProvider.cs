namespace PermitQL.Abstractions;

using Models;

public interface IRulesProvider
{
    RuleSet GetRuleSet(string key);

    IReadOnlyList<string> GetAvailableKeys();
}