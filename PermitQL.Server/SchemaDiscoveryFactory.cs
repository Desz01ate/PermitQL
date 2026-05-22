namespace PermitQL.Server;

using System.Data.Common;
using Abstractions;
using PermitQL.Abstractions;
using Implementations.DiscoveryService;

public static class SchemaDiscoveryFactory
{
    public static ISchemaDiscovery Create(SqlDialect provider, Func<DbConnection> connectionFactory)
    {
        return provider switch
        {
            SqlDialect.Sqlite => new SqliteDiscovery(connectionFactory),
            SqlDialect.PostgreSql => new PostgresqlDiscovery(connectionFactory),
            _ => throw new ArgumentException($"Unsupported provider: {provider}", nameof(provider)),
        };
    }
}