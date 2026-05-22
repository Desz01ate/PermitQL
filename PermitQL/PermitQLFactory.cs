namespace PermitQL;

using System.Data.Common;
using Abstractions;
using Data;
using Data.Resolvers;
using Parsing;
using Rewriting.Dialects;
using Rules;
using Validation;

public class PermitQLFactory : IPermitQLFactory
{
    public SqlDialect Dialect { get; }

    public PermitQLFactory(SqlDialect dialect)
    {
        this.Dialect = dialect;
    }

    public virtual ISqlAstProvider CreateSqlAstProvider() => new SqlAstProvider();

    public virtual IRulesProvider CreateRulesProvider(string rulesDirectory)
        => new YamlRulesProvider(rulesDirectory);

    public virtual IQueryValidator CreateQueryValidator() => new QueryValidator();

    public virtual IQueryRewriter CreateQueryRewriter(IDataAccessor dataAccessor)
    {
        return this.Dialect switch
        {
            SqlDialect.PostgreSql or SqlDialect.Sqlite => new LimitQueryRewriter(dataAccessor),
            SqlDialect.SqlServer => new TopQueryRewriter(dataAccessor),
            _ => throw new ArgumentOutOfRangeException(nameof(this.Dialect), this.Dialect,
                $"Unsupported SQL dialect: {this.Dialect}"),
        };
    }

    public virtual IForeignKeyResolver CreateForeignKeyResolver()
    {
        return this.Dialect switch
        {
            SqlDialect.PostgreSql => new PostgresForeignKeyResolver(),
            SqlDialect.Sqlite => new SqliteForeignKeyResolver(),
            SqlDialect.SqlServer => NullForeignKeyResolver.Instance,
            _ => throw this.CreateUnsupportedMetadataDialectException(),
        };
    }

    public virtual IConstraintResolver CreateConstraintResolver()
    {
        return this.Dialect switch
        {
            SqlDialect.PostgreSql => new PostgresConstraintResolver(),
            SqlDialect.Sqlite => new SqliteConstraintResolver(),
            SqlDialect.SqlServer => NullConstraintResolver.Instance,
            _ => throw this.CreateUnsupportedMetadataDialectException(),
        };
    }

    public virtual IRelationshipResolver CreateRelationshipResolver()
    {
        return this.Dialect switch
        {
            SqlDialect.PostgreSql => new PostgresRelationshipResolver(),
            SqlDialect.Sqlite => new SqliteRelationshipResolver(),
            SqlDialect.SqlServer => NullRelationshipResolver.Instance,
            _ => throw this.CreateUnsupportedMetadataDialectException(),
        };
    }

    public virtual IIndexResolver CreateIndexResolver()
    {
        return this.Dialect switch
        {
            SqlDialect.PostgreSql => new PostgresIndexResolver(),
            SqlDialect.Sqlite => new SqliteIndexResolver(),
            SqlDialect.SqlServer => NullIndexResolver.Instance,
            _ => throw this.CreateUnsupportedMetadataDialectException(),
        };
    }

    public virtual IStatisticsResolver CreateStatisticsResolver()
    {
        return this.Dialect switch
        {
            SqlDialect.PostgreSql => new PostgresStatisticsResolver(),
            SqlDialect.Sqlite => new SqliteStatisticsResolver(),
            SqlDialect.SqlServer => NullStatisticsResolver.Instance,
            _ => throw this.CreateUnsupportedMetadataDialectException(),
        };
    }

    public virtual IProviderCapabilityResolver CreateProviderCapabilityResolver()
    {
        return this.Dialect switch
        {
            SqlDialect.PostgreSql => new PostgresProviderCapabilityResolver(),
            SqlDialect.Sqlite => new SqliteProviderCapabilityResolver(),
            SqlDialect.SqlServer => NullProviderCapabilityResolver.Instance,
            _ => throw this.CreateUnsupportedMetadataDialectException(),
        };
    }

    public virtual IDataAccessor CreateDataAccessor(Func<DbConnection> connectionFactory)
        => new AdoNetDataAccessor(
            connectionFactory, 
            this.CreateConstraintResolver(), 
            this.CreateRelationshipResolver(), 
            this.CreateIndexResolver(), 
            this.CreateStatisticsResolver(), 
            this.CreateProviderCapabilityResolver());

    public virtual IQueryPipeline CreatePipeline(
        IRulesProvider rulesProvider,
        ISqlAstProvider astProvider,
        IQueryValidator validator,
        IQueryRewriter rewriter,
        IDataAccessor dataAccessor)
        => new QueryPipeline(rulesProvider, astProvider, validator, rewriter, dataAccessor);

    private ArgumentOutOfRangeException CreateUnsupportedMetadataDialectException()
        => new(nameof(this.Dialect), this.Dialect, $"Unsupported SQL dialect for metadata resolvers: {this.Dialect}");
}
