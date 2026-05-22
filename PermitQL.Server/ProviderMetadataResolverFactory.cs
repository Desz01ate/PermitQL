namespace PermitQL.Server;

using PermitQL.Abstractions;
using PermitQL.Data;
using PermitQL.Server.Implementations.MetadataResolvers;

public static class ProviderMetadataResolverFactory
{
    public static (IConstraintResolver Constraints, IRelationshipResolver Relationships, IIndexResolver Indexes, IStatisticsResolver Statistics, IProviderCapabilityResolver Capabilities) Create(SqlDialect dialect)
        => dialect switch
        {
            SqlDialect.PostgreSql => (
                new PostgresConstraintResolver(),
                new PostgresRelationshipResolver(),
                new PostgresIndexResolver(),
                new PostgresStatisticsResolver(),
                NullProviderCapabilityResolver.Instance),
            SqlDialect.Sqlite => (
                new SqliteConstraintResolver(),
                new SqliteRelationshipResolver(),
                new SqliteIndexResolver(),
                new SqliteStatisticsResolver(),
                NullProviderCapabilityResolver.Instance),
            _ => (
                NullConstraintResolver.Instance,
                NullRelationshipResolver.Instance,
                NullIndexResolver.Instance,
                NullStatisticsResolver.Instance,
                NullProviderCapabilityResolver.Instance),
        };
}
