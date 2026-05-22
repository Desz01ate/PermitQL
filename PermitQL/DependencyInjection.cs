namespace PermitQL;

using System.Data.Common;
using Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddPermitQL(
        this IServiceCollection services,
        string rulesDirectory,
        Func<DbConnection> connectionFactory)
    {
        return AddPermitQL(services, rulesDirectory, connectionFactory, SqlDialect.PostgreSql, fkResolver: null);
    }

    public static IServiceCollection AddPermitQL(
        this IServiceCollection services,
        string rulesDirectory,
        Func<DbConnection> connectionFactory,
        SqlDialect dialect,
        IForeignKeyResolver? fkResolver = null)
    {
        return AddPermitQL(services, rulesDirectory, connectionFactory,
            new PermitQLFactory(dialect), fkResolver);
    }

    public static IServiceCollection AddPermitQL(
        this IServiceCollection services,
        string rulesDirectory,
        Func<DbConnection> connectionFactory,
        IPermitQLFactory factory,
        IForeignKeyResolver? fkResolver = null)
    {
        var astProvider = factory.CreateSqlAstProvider();
        var rulesProvider = factory.CreateRulesProvider(rulesDirectory);
        var dataAccessor = factory.CreateDataAccessor(connectionFactory, fkResolver);
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
        Func<DbConnection> connectionFactory,
        SqlDialect dialect,
        IConstraintResolver? constraintResolver = null,
        IRelationshipResolver? relationshipResolver = null,
        IIndexResolver? indexResolver = null,
        IStatisticsResolver? statisticsResolver = null,
        IProviderCapabilityResolver? capabilityResolver = null)
    {
        return AddPermitQL(services, rulesDirectory, connectionFactory,
            new PermitQLFactory(dialect), constraintResolver, relationshipResolver,
            indexResolver, statisticsResolver, capabilityResolver);
    }

    public static IServiceCollection AddPermitQL(
        this IServiceCollection services,
        string rulesDirectory,
        Func<DbConnection> connectionFactory,
        IPermitQLFactory factory,
        IConstraintResolver? constraintResolver = null,
        IRelationshipResolver? relationshipResolver = null,
        IIndexResolver? indexResolver = null,
        IStatisticsResolver? statisticsResolver = null,
        IProviderCapabilityResolver? capabilityResolver = null)
    {
        var astProvider = factory.CreateSqlAstProvider();
        var rulesProvider = factory.CreateRulesProvider(rulesDirectory);
        var dataAccessor = factory.CreateDataAccessor(connectionFactory,
            constraintResolver, relationshipResolver, indexResolver,
            statisticsResolver, capabilityResolver);
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
}