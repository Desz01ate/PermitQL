namespace PermitQL.Server.Models;

public sealed record QueryResponse(IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<object?[]> Rows, int RowCount);

public sealed record ColumnInfo(string Name, string Type, bool IsNullable);