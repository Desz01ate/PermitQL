namespace PermitQL.Tests;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Data;
using PermitQL.Rewriting.Dialects;
using NSubstitute;

public class PermitQLFactoryTests
{
    [Theory]
    [InlineData(SqlDialect.PostgreSql)]
    [InlineData(SqlDialect.Sqlite)]
    public void CreateQueryRewriter_ForLimitDialects_ReturnsLimitQueryRewriter(SqlDialect dialect)
    {
        var factory = new PermitQLFactory(dialect);
        var rewriter = factory.CreateQueryRewriter(Substitute.For<IDataAccessor>());
        Assert.IsType<LimitQueryRewriter>(rewriter);
    }

    [Fact]
    public void CreateQueryRewriter_ForSqlServer_ReturnsTopQueryRewriter()
    {
        var factory = new PermitQLFactory(SqlDialect.SqlServer);
        var rewriter = factory.CreateQueryRewriter(Substitute.For<IDataAccessor>());
        Assert.IsType<TopQueryRewriter>(rewriter);
    }

    [Fact]
    public void CreateSqlAstProvider_ReturnsInstance()
    {
        var factory = new PermitQLFactory(SqlDialect.PostgreSql);
        var provider = factory.CreateSqlAstProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void CreateQueryValidator_ReturnsInstance()
    {
        var factory = new PermitQLFactory(SqlDialect.PostgreSql);
        var validator = factory.CreateQueryValidator();
        Assert.NotNull(validator);
    }

    [Fact]
    public void CreateDataAccessor_WithMetadataResolvers_ReturnsAccessor()
    {
        var factory = new PermitQLFactory(SqlDialect.PostgreSql);

        var accessor = factory.CreateDataAccessor(
            () => Substitute.For<DbConnection>(),
            Substitute.For<IConstraintResolver>(),
            Substitute.For<IRelationshipResolver>(),
            Substitute.For<IIndexResolver>(),
            Substitute.For<IStatisticsResolver>(),
            Substitute.For<IProviderCapabilityResolver>());

        Assert.IsType<AdoNetDataAccessor>(accessor);
    }
}
