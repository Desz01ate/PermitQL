namespace PermitQL.Models;

using SqlParser.Ast;

public enum StatementKind
{
    Select,
    Insert,
    Update,
    Delete,
    Other
}

public record QualifiedTableName(string? Schema, string Table)
{
    public override string ToString() => this.Schema is not null ? $"{this.Schema}.{this.Table}" : this.Table;
}

public record QualifiedColumnName(string? Schema, string? Table, string Column)
{
    public override string ToString() =>
        (this.Schema, this.Table) switch
        {
            (not null, not null) => $"{this.Schema}.{this.Table}.{this.Column}",
            (null, not null) => $"{this.Table}.{this.Column}",
            _ => this.Column,
        };
}

public record CteDefinition(
    string Name,
    IReadOnlyList<string>? ColumnAliases,
    IReadOnlySet<QualifiedTableName> InnerReferencedTables,
    IReadOnlySet<QualifiedColumnName> InnerReferencedColumns,
    IReadOnlyDictionary<string, string> InnerAliasMap);

public record ParsedQuery(
    Statement Statement,
    IReadOnlySet<QualifiedTableName> ReferencedTables,
    IReadOnlySet<QualifiedColumnName> ReferencedColumns,
    StatementKind StatementType,
    QualifiedTableName? MutationTarget = null,
    IReadOnlyList<CteDefinition>? CteDefinitions = null)
{
    public Statement.Select AsSelect() => this.Statement as Statement.Select
        ?? throw new InvalidOperationException("Statement is not a SELECT.");

    public Query Query => this.AsSelect().Query;
}