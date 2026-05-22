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
    public override string ToString() => Schema is not null ? $"{Schema}.{Table}" : Table;
}

public record QualifiedColumnName(string? Schema, string? Table, string Column)
{
    public override string ToString() =>
        (Schema, Table) switch
        {
            (not null, not null) => $"{Schema}.{Table}.{Column}",
            (null, not null) => $"{Table}.{Column}",
            _ => Column,
        };
}

public record ParsedQuery(
    Statement Statement,
    IReadOnlySet<QualifiedTableName> ReferencedTables,
    IReadOnlySet<QualifiedColumnName> ReferencedColumns,
    StatementKind StatementType,
    QualifiedTableName? MutationTarget = null)
{
    public Statement.Select AsSelect() =>
        Statement as Statement.Select
        ?? throw new InvalidOperationException("Statement is not a SELECT.");

    public Query Query => AsSelect().Query;
}