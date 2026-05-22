namespace PermitQL;

using System.Data.Common;
using Abstractions;
using Data;
using Parsing;
using Rewriting.Dialects;
using Rules;
using Validation;

public class PermitQLFactory : IPermitQLFactory
{
    public SqlDialect Dialect { get; }

    public PermitQLFactory(SqlDialect dialect)
    {
        Dialect = dialect;
    }

    public virtual ISqlAstProvider CreateSqlAstProvider() => new SqlAstProvider();

    public virtual IRulesProvider CreateRulesProvider(string rulesDirectory)
        => new YamlRulesProvider(rulesDirectory);

    public virtual IQueryValidator CreateQueryValidator() => new QueryValidator();

    public virtual IQueryRewriter CreateQueryRewriter(IDataAccessor dataAccessor)
    {
        return Dialect switch
        {
            SqlDialect.PostgreSql or SqlDialect.Sqlite => new LimitQueryRewriter(dataAccessor),
            SqlDialect.SqlServer => new TopQueryRewriter(dataAccessor),
            _ => throw new ArgumentOutOfRangeException(nameof(Dialect), Dialect,
                $"Unsupported SQL dialect: {Dialect}"),
        };
    }

    public virtual IDataAccessor CreateDataAccessor(Func<DbConnection> connectionFactory, IForeignKeyResolver? fkResolver)
        => new AdoNetDataAccessor(connectionFactory, fkResolver);

    public virtual IDataAccessor CreateDataAccessor(
        Func<DbConnection> connectionFactory,
        IConstraintResolver? constraintResolver = null,
        IRelationshipResolver? relationshipResolver = null,
        IIndexResolver? indexResolver = null,
        IStatisticsResolver? statisticsResolver = null,
        IProviderCapabilityResolver? capabilityResolver = null)
        => new AdoNetDataAccessor(
            connectionFactory,
            constraintResolver,
            relationshipResolver,
            indexResolver,
            statisticsResolver,
            capabilityResolver);

    public virtual IQueryPipeline CreatePipeline(
        IRulesProvider rulesProvider,
        ISqlAstProvider astProvider,
        IQueryValidator validator,
        IQueryRewriter rewriter,
        IDataAccessor dataAccessor)
        => new QueryPipeline(rulesProvider, astProvider, validator, rewriter, dataAccessor);
}
