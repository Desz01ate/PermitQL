namespace PermitQL.Models;

public class RuleSet
{
    public required string Version { get; init; }

    public required string Database { get; init; }

    public required GlobalLimits GlobalLimits { get; init; }

    public required Dictionary<string, SchemaRule> ExposedSchemas { get; init; }
}

public class GlobalLimits
{
    private static readonly string[] DefaultAllowedOperations = ["select"];
    private static readonly StringComparer OpComparer = StringComparer.OrdinalIgnoreCase;

    public required int MaxRowsReturned { get; init; }

    public required int TimeoutMs { get; init; }

    public string[] AllowedOperations { get; init; } = DefaultAllowedOperations;

    public bool IsOperationAllowed(StatementKind kind)
    {
        return AllowedOperations.Contains(KindToString(kind), OpComparer);
    }

    internal static string KindToString(StatementKind kind) => kind switch
    {
        StatementKind.Select => "select",
        StatementKind.Insert => "insert",
        StatementKind.Update => "update",
        StatementKind.Delete => "delete",
        _ => string.Empty,
    };
}

public class SchemaRule
{
    public required Dictionary<string, TableRule> Tables { get; init; }
}

public class TableRule
{
    private static readonly StringComparer OpComparer = StringComparer.OrdinalIgnoreCase;

    public required string[] AllowedColumns { get; init; }

    public string[]? DeniedColumns { get; init; }

    public string? RowFilter { get; init; }

    public string[]? AllowedOperations { get; init; }

    public string? TableSemanticDescription { get; init; }

    public Dictionary<string, string> ColumnSemanticDescriptions { get; init; } = [];

    public bool IsWildcard => AllowedColumns is ["*"];

    public bool IsOperationAllowed(StatementKind kind, GlobalLimits globalLimits)
    {
        var ops = AllowedOperations ?? globalLimits.AllowedOperations;
        return ops.Contains(GlobalLimits.KindToString(kind), OpComparer);
    }

    public bool IsColumnAllowed(string column)
    {
        if (!IsWildcard)
            return AllowedColumns.Contains(column, StringComparer.OrdinalIgnoreCase);

        if (DeniedColumns is null)
            return true;

        return !DeniedColumns.Contains(column, StringComparer.OrdinalIgnoreCase);
    }
}