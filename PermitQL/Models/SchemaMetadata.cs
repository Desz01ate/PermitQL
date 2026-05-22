namespace PermitQL.Models;

public enum CapabilitySupport
{
    Supported,
    Unsupported,
    Unknown,
}

public enum GenerationKind
{
    None,
    Identity,
    AutoIncrement,
    Computed,
    Unknown,
}

public sealed record SchemaColumnMetadata(
    string Name,
    string Type,
    bool IsNullable,
    bool IsPrimaryKey,
    string? DefaultValue,
    bool IsGenerated,
    GenerationKind GenerationKind);

public sealed record UniqueConstraintMetadata(string Name, IReadOnlyList<string> Columns);

public sealed record CheckConstraintMetadata(string Name, string Expression);

public sealed record TableConstraintMetadata(
    IReadOnlyList<UniqueConstraintMetadata> Unique,
    IReadOnlyList<CheckConstraintMetadata> Check);

public sealed record ForeignKeyMetadata(
    string ConstraintName,
    string FromSchema,
    string FromTable,
    string FromColumn,
    string ToSchema,
    string ToTable,
    string ToColumn,
    string? OnDelete,
    string? OnUpdate);

public sealed record TableIndexMetadata(string Name, IReadOnlyList<string> Columns, bool IsUnique);

public sealed record TableStatisticsMetadata(long? ApproximateRowCount, DateTimeOffset? LastAnalyzed);

public sealed record QueryCapabilityMetadata(
    CapabilitySupport Ctes,
    CapabilitySupport Subqueries,
    CapabilitySupport DerivedTables,
    CapabilitySupport WindowFunctions,
    CapabilitySupport Mutations,
    IReadOnlyList<string> Notes);
