namespace PermitQL.Rules;

using Abstractions;
using Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public sealed class YamlRulesProvider : IRulesProvider
{
    private readonly Dictionary<string, RuleSet> _ruleSets;

    public YamlRulesProvider(string rulesDirectory)
    {
        var deserializer = new DeserializerBuilder()
                           .WithNamingConvention(UnderscoredNamingConvention.Instance)
                           .Build();

        _ruleSets = new Dictionary<string, RuleSet>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(rulesDirectory, "*.yaml"))
        {
            var yaml = File.ReadAllText(file);
            var ruleSet = deserializer.Deserialize<RuleSet>(yaml);
            _ruleSets[ruleSet.Database] = ruleSet;
        }

        foreach (var file in Directory.EnumerateFiles(rulesDirectory, "*.yml"))
        {
            var yaml = File.ReadAllText(file);
            var ruleSet = deserializer.Deserialize<RuleSet>(yaml);
            _ruleSets[ruleSet.Database] = ruleSet;
        }
    }

    public RuleSet GetRuleSet(string key)
    {
        if (_ruleSets.TryGetValue(key, out var ruleSet))
            return ruleSet;
        throw new KeyNotFoundException($"No rule set found for key '{key}'. Available keys: {string.Join(", ", _ruleSets.Keys)}");
    }

    public IReadOnlyList<string> GetAvailableKeys() => _ruleSets.Keys.ToList();
}