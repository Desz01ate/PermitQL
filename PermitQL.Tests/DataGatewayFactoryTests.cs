namespace PermitQL.Tests;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Data;
using PermitQL.Models;
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

    [Theory]
    [InlineData(SqlDialect.PostgreSql)]
    [InlineData(SqlDialect.Sqlite)]
    public void CreateDataAccessor_ForSupportedMetadataDialects_ReturnsAccessor(SqlDialect dialect)
    {
        var factory = new PermitQLFactory(dialect);

        var accessor = factory.CreateDataAccessor(() => Substitute.For<DbConnection>());

        Assert.IsType<AdoNetDataAccessor>(accessor);
    }

    [Fact]
    public void CreateDataAccessor_ForSqlServer_ReturnsAccessorWithNullResolvers()
    {
        var factory = new PermitQLFactory(SqlDialect.SqlServer);

        var accessor = factory.CreateDataAccessor(() => Substitute.For<DbConnection>());

        Assert.IsType<AdoNetDataAccessor>(accessor);
    }

    [Fact]
    public void ResolverCreationMethods_AreAvailableOnInterface()
    {
        IPermitQLFactory factory = new PermitQLFactory(SqlDialect.PostgreSql);

        Assert.NotNull(factory.CreateForeignKeyResolver());
        Assert.NotNull(factory.CreateConstraintResolver());
        Assert.NotNull(factory.CreateRelationshipResolver());
        Assert.NotNull(factory.CreateIndexResolver());
        Assert.NotNull(factory.CreateStatisticsResolver());
        Assert.NotNull(factory.CreateProviderCapabilityResolver());
    }

    [Theory]
    [InlineData(
        SqlDialect.PostgreSql,
        CapabilitySupport.Supported,
        CapabilitySupport.Supported,
        CapabilitySupport.Supported,
        CapabilitySupport.Supported,
        CapabilitySupport.Supported)]
    [InlineData(
        SqlDialect.Sqlite,
        CapabilitySupport.Supported,
        CapabilitySupport.Supported,
        CapabilitySupport.Supported,
        CapabilitySupport.Supported,
        CapabilitySupport.Supported)]
    public async Task CreateProviderCapabilityResolver_ForSupportedDialects_ReturnsConcreteCapabilities(
        SqlDialect dialect,
        CapabilitySupport expectedCtes,
        CapabilitySupport expectedSubqueries,
        CapabilitySupport expectedDerivedTables,
        CapabilitySupport expectedWindowFunctions,
        CapabilitySupport expectedMutations)
    {
        var factory = new PermitQLFactory(dialect);

        var capabilities = await factory.CreateProviderCapabilityResolver().ResolveAsync();

        Assert.Equal(expectedCtes, capabilities.Ctes);
        Assert.Equal(expectedSubqueries, capabilities.Subqueries);
        Assert.Equal(expectedDerivedTables, capabilities.DerivedTables);
        Assert.Equal(expectedWindowFunctions, capabilities.WindowFunctions);
        Assert.Equal(expectedMutations, capabilities.Mutations);
        Assert.NotEmpty(capabilities.Notes);
    }

    [Fact]
    public async Task CreateDataAccessor_UsesOverriddenResolverHooks()
    {
        var connection = Substitute.For<DbConnection>();
        connection.OpenAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var constraintResolver = Substitute.For<IConstraintResolver>();
        constraintResolver.ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));

        var relationshipResolver = Substitute.For<IRelationshipResolver>();
        relationshipResolver.ResolveOutboundAsync(connection, "public", "orders", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<ForeignKeyMetadata>>([]));

        var indexResolver = Substitute.For<IIndexResolver>();
        indexResolver.ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<TableIndexMetadata>>([]));

        var statisticsResolver = Substitute.For<IStatisticsResolver>();
        statisticsResolver.ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(null, null));

        var capabilityResolver = Substitute.For<IProviderCapabilityResolver>();
        capabilityResolver.ResolveAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported,
                CapabilitySupport.Supported,
                CapabilitySupport.Supported,
                CapabilitySupport.Unknown,
                CapabilitySupport.Unsupported,
                []));

        var factory = new TestPermitQLFactory(
            SqlDialect.PostgreSql,
            constraintResolver,
            relationshipResolver,
            indexResolver,
            statisticsResolver,
            capabilityResolver);

        var accessor = factory.CreateDataAccessor(() => connection);

        await accessor.GetTableConstraintsAsync("public", "orders");
        await accessor.GetOutboundForeignKeysAsync("public", "orders");
        await accessor.GetTableIndexesAsync("public", "orders");
        await accessor.GetTableStatisticsAsync("public", "orders");
        await accessor.GetQueryCapabilitiesAsync();

        await constraintResolver.Received(1).ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>());
        await relationshipResolver.Received(1).ResolveOutboundAsync(connection, "public", "orders", Arg.Any<CancellationToken>());
        await indexResolver.Received(1).ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>());
        await statisticsResolver.Received(1).ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>());
        await capabilityResolver.Received(1).ResolveAsync(Arg.Any<CancellationToken>());
    }

    private sealed class TestPermitQLFactory : PermitQLFactory
    {
        private readonly IConstraintResolver _constraintResolver;
        private readonly IRelationshipResolver _relationshipResolver;
        private readonly IIndexResolver _indexResolver;
        private readonly IStatisticsResolver _statisticsResolver;
        private readonly IProviderCapabilityResolver _capabilityResolver;

        public TestPermitQLFactory(
            SqlDialect dialect,
            IConstraintResolver constraintResolver,
            IRelationshipResolver relationshipResolver,
            IIndexResolver indexResolver,
            IStatisticsResolver statisticsResolver,
            IProviderCapabilityResolver capabilityResolver)
            : base(dialect)
        {
            _constraintResolver = constraintResolver;
            _relationshipResolver = relationshipResolver;
            _indexResolver = indexResolver;
            _statisticsResolver = statisticsResolver;
            _capabilityResolver = capabilityResolver;
        }

        public override IForeignKeyResolver CreateForeignKeyResolver() => NullForeignKeyResolver.Instance;

        public override IConstraintResolver CreateConstraintResolver() => _constraintResolver;

        public override IRelationshipResolver CreateRelationshipResolver() => _relationshipResolver;

        public override IIndexResolver CreateIndexResolver() => _indexResolver;

        public override IStatisticsResolver CreateStatisticsResolver() => _statisticsResolver;

        public override IProviderCapabilityResolver CreateProviderCapabilityResolver() => _capabilityResolver;
    }
}
