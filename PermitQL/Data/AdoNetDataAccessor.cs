namespace PermitQL.Data;

using System.Data.Common;
using System.Runtime.CompilerServices;
using Abstractions;
using Models;

public sealed class AdoNetDataAccessor : IDataAccessor
{
    private readonly Func<DbConnection> _connectionFactory;
    private readonly IConstraintResolver _constraintResolver;
    private readonly IRelationshipResolver _relationshipResolver;
    private readonly IIndexResolver _indexResolver;
    private readonly IStatisticsResolver _statisticsResolver;
    private readonly IProviderCapabilityResolver _capabilityResolver;

    public AdoNetDataAccessor(
        Func<DbConnection> connectionFactory,
        IConstraintResolver? constraintResolver = null,
        IRelationshipResolver? relationshipResolver = null,
        IIndexResolver? indexResolver = null,
        IStatisticsResolver? statisticsResolver = null,
        IProviderCapabilityResolver? capabilityResolver = null)
    {
        _connectionFactory = connectionFactory;
        _constraintResolver = constraintResolver ?? NullConstraintResolver.Instance;
        _relationshipResolver = relationshipResolver ?? NullRelationshipResolver.Instance;
        _indexResolver = indexResolver ?? NullIndexResolver.Instance;
        _statisticsResolver = statisticsResolver ?? NullStatisticsResolver.Instance;
        _capabilityResolver = capabilityResolver ?? NullProviderCapabilityResolver.Instance;
    }

    public AdoNetDataAccessor(Func<DbConnection> connectionFactory, IForeignKeyResolver? fkResolver)
        : this(
            connectionFactory,
            relationshipResolver: fkResolver is null ? null : new ForeignKeyRelationshipResolver(fkResolver))
    {
    }

    public async ValueTask<IReadOnlyList<ColumnDefinition>> GetColumnDefinitionAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync(
            System.Data.CommandBehavior.SchemaOnly | System.Data.CommandBehavior.KeyInfo,
            cancellationToken);
        var schema = reader.GetColumnSchema();
        var columns = new List<ColumnDefinition>(schema.Count);

        for (var i = 0; i < schema.Count; i++)
        {
            var col = schema[i];
            columns.Add(new ColumnDefinition(
                Index: i,
                Name: col.ColumnName,
                Type: col.DataTypeName ?? "unknown",
                IsNullable: col.AllowDBNull ?? true,
                IsKey: col.IsKey ?? false));
        }

        return columns;
    }

    public async ValueTask<IReadOnlyList<SchemaColumnMetadata>> GetTableColumnsAsync(
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{schema}\".\"{table}\" WHERE 1=0";
        await using var reader = await command.ExecuteReaderAsync(
            System.Data.CommandBehavior.SchemaOnly | System.Data.CommandBehavior.KeyInfo,
            cancellationToken);

        var columnSchema = reader.GetColumnSchema();
        var columns = new List<SchemaColumnMetadata>(columnSchema.Count);
        columns.AddRange(columnSchema.Select(col => new SchemaColumnMetadata(
            Name: col.ColumnName,
            Type: col.DataTypeName ?? "unknown",
            IsNullable: col.AllowDBNull ?? true,
            IsPrimaryKey: col.IsKey ?? false,
            DefaultValue: null,
            IsGenerated: false,
            GenerationKind: GenerationKind.None)));

        return columns;
    }

    public async ValueTask<TableConstraintMetadata> GetTableConstraintsAsync(
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        return await _constraintResolver.ResolveAsync(connection, schema, table, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> GetOutboundForeignKeysAsync(
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        return await _relationshipResolver.ResolveOutboundAsync(connection, schema, table, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ForeignKeyMetadata>> GetInboundForeignKeysAsync(
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        return await _relationshipResolver.ResolveInboundAsync(connection, schema, table, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<TableIndexMetadata>> GetTableIndexesAsync(
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        return await _indexResolver.ResolveAsync(connection, schema, table, cancellationToken);
    }

    public async ValueTask<TableStatisticsMetadata> GetTableStatisticsAsync(
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        return await _statisticsResolver.ResolveAsync(connection, schema, table, cancellationToken);
    }

    public ValueTask<QueryCapabilityMetadata> GetQueryCapabilitiesAsync(CancellationToken cancellationToken = default)
        => _capabilityResolver.ResolveAsync(cancellationToken);

    public async IAsyncEnumerable<object?[]> QueryAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var fieldCount = reader.FieldCount;

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new object?[fieldCount];
            for (var i = 0; i < fieldCount; i++)
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            yield return values;
        }
    }

    private sealed class ForeignKeyRelationshipResolver : IRelationshipResolver
    {
        private readonly IForeignKeyResolver _fkResolver;

        public ForeignKeyRelationshipResolver(IForeignKeyResolver fkResolver)
        {
            _fkResolver = fkResolver;
        }

        public ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveOutboundAsync(
            DbConnection connection,
            string schema,
            string table,
            CancellationToken cancellationToken = default)
            => _fkResolver.ResolveAsync(connection, schema, table, cancellationToken);

        public ValueTask<IReadOnlyList<ForeignKeyMetadata>> ResolveInboundAsync(
            DbConnection connection,
            string schema,
            string table,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ForeignKeyMetadata>>([]);
    }
}
