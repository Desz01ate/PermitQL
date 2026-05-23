namespace PermitQL.Server;

using System.Data.Common;
using PermitQL.Abstractions;
using Microsoft.Data.Sqlite;
using Npgsql;
using Pgvector.Npgsql;

public static class ConnectionFactory
{
    public static Func<DbConnection> Create(SqlDialect provider, string connectionString) => provider switch
    {
        SqlDialect.PostgreSql => CreatePostgresFactory(connectionString),
        SqlDialect.Sqlite => () => new SqliteConnection(connectionString),
        _ => throw new ArgumentException($"Unsupported database provider: '{provider}'. Supported: postgresql, sqlite.", nameof(provider)),
    };

    private static Func<DbConnection> CreatePostgresFactory(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseGeoJson();
        builder.UseVector();
        var dataSource = builder.Build();

        return dataSource.CreateConnection;
    }
}