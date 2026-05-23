namespace PermitQL.Abstractions;

using System.Data.Common;

public interface IPermitQLFactory
{
    SqlDialect Dialect { get; }

    ISqlAstProvider CreateSqlAstProvider();

    IRulesProvider CreateRulesProvider(string rulesDirectory);

    IQueryValidator CreateQueryValidator();

    IQueryRewriter CreateQueryRewriter(IDataAccessor dataAccessor);

    IForeignKeyResolver CreateForeignKeyResolver();

    IConstraintResolver CreateConstraintResolver();

    IRelationshipResolver CreateRelationshipResolver();

    IIndexResolver CreateIndexResolver();

    IStatisticsResolver CreateStatisticsResolver();

    IProviderCapabilityResolver CreateProviderCapabilityResolver();

    IDataAccessor CreateDataAccessor(Func<DbConnection> connectionFactory, int? commandTimeoutSeconds = null);

    IQueryPipeline CreatePipeline(
        IRulesProvider rulesProvider,
        ISqlAstProvider astProvider,
        IQueryValidator validator,
        IQueryRewriter rewriter,
        IDataAccessor dataAccessor);
}
