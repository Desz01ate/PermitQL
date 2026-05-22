namespace PermitQL;

using System.Data.Common;
using Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddPermitQL(
        this IServiceCollection services,
        string rulesDirectory,
        Func<DbConnection> connectionFactory,
        SqlDialect dialect)
    {
        return AddPermitQL(services, rulesDirectory, connectionFactory,
            new PermitQLFactory(dialect));
    }

    public static IServiceCollection AddPermitQL(
        this IServiceCollection services,
        string rulesDirectory,
        Func<DbConnection> connectionFactory,
        IPermitQLFactory factory)
    {
        var astProvider = factory.CreateSqlAstProvider();
        var rulesProvider = factory.CreateRulesProvider(rulesDirectory);
        var dataAccessor = factory.CreateDataAccessor(connectionFactory);
        var validator = factory.CreateQueryValidator();
        var rewriter = factory.CreateQueryRewriter(dataAccessor);

        services.AddSingleton(factory);
        services.AddSingleton(astProvider);
        services.AddSingleton(rulesProvider);
        services.AddSingleton(dataAccessor);
        services.AddSingleton(validator);
        services.AddSingleton(rewriter);
        services.AddSingleton(factory.CreatePipeline(
            rulesProvider, astProvider, validator, rewriter, dataAccessor));

        return services;
    }

    public static IServiceCollection AddPermitQL(
        this IServiceCollection services,
        string rulesDirectory,
        Func<DbConnection> connectionFactory)
        => AddPermitQL(services, rulesDirectory, connectionFactory, SqlDialect.PostgreSql);
}
