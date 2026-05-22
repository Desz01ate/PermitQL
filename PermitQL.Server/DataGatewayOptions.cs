namespace PermitQL.Server;

using PermitQL.Abstractions;

public sealed class PermitQLOptions
{
    public const string SectionName = "PermitQL";

    public required string RulesDirectory { get; init; }

    public required string ConnectionString { get; init; }

    public required SqlDialect Provider { get; init; } = SqlDialect.PostgreSql;
}