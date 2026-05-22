namespace PermitQL.Server;

using PermitQL.Abstractions;
using PermitQL.Data;
using Implementations.ForeignKeyResolvers;

public static class ForeignKeyResolverFactory
{
    public static IForeignKeyResolver Create(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => new PostgresForeignKeyResolver(),
        SqlDialect.Sqlite => new SqliteForeignKeyResolver(),
        _ => NullForeignKeyResolver.Instance,
    };
}
