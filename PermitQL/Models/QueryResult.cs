namespace PermitQL.Models;

public record ColumnDefinition(int Index, string Name, string Type, bool IsNullable, bool IsKey = false);

public record QueryResult(IReadOnlyList<ColumnDefinition> Columns, IReadOnlyList<object?[]> Rows);
