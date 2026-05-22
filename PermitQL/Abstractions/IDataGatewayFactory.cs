namespace PermitQL.Abstractions;

using System.Data.Common;

public interface IPermitQLFactory
{
    SqlDialect Dialect { get; }

    ISqlAstProvider CreateSqlAstProvider();

    IRulesProvider CreateRulesProvider(string rulesDirectory);

    IQueryValidator CreateQueryValidator();

    IQueryRewriter CreateQueryRewriter(IDataAccessor dataAccessor);

    IDataAccessor CreateDataAccessor(Func<DbConnection> connectionFactory, IForeignKeyResolver? fkResolver);

    IDataAccessor CreateDataAccessor(
        Func<DbConnection> connectionFactory,
        IConstraintResolver? constraintResolver = null,
        IRelationshipResolver? relationshipResolver = null,
        IIndexResolver? indexResolver = null,
        IStatisticsResolver? statisticsResolver = null,
        IProviderCapabilityResolver? capabilityResolver = null);

    IQueryPipeline CreatePipeline(
        IRulesProvider rulesProvider,
        ISqlAstProvider astProvider,
        IQueryValidator validator,
        IQueryRewriter rewriter,
        IDataAccessor dataAccessor);
}
