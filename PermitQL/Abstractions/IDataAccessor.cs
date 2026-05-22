namespace PermitQL.Abstractions;

using Models;

public interface IDataAccessor
{
    ValueTask<IReadOnlyList<ColumnDefinition>> GetColumnDefinitionAsync(string query, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SchemaColumnMetadata>> GetTableColumnsAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<TableConstraintMetadata> GetTableConstraintsAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ForeignKeyMetadata>> GetOutboundForeignKeysAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ForeignKeyMetadata>> GetInboundForeignKeysAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TableIndexMetadata>> GetTableIndexesAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<TableStatisticsMetadata> GetTableStatisticsAsync(string schema, string table, CancellationToken cancellationToken = default);

    ValueTask<QueryCapabilityMetadata> GetQueryCapabilitiesAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<object?[]> QueryAsync(string query, CancellationToken cancellationToken = default);
}
