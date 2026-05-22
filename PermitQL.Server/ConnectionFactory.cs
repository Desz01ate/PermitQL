namespace PermitQL.Server;

using System.Data.Common;
using PermitQL.Abstractions;
using Microsoft.Data.Sqlite;
using Npgsql;

public static class ConnectionFactory
{
    public static Func<DbConnection> Create(SqlDialect provider, string connectionString) => provider switch
    {
        SqlDialect.PostgreSql => () => new NpgsqlConnection(connectionString),
        SqlDialect.Sqlite => () => new SqliteConnection(connectionString),
        _ => throw new ArgumentException($"Unsupported database provider: '{provider}'. Supported: postgresql, sqlite.", nameof(provider)),
    };
}