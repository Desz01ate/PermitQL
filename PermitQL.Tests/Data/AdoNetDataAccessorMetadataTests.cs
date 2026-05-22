namespace PermitQL.Tests.Data;

using System.Data.Common;
using PermitQL.Abstractions;
using PermitQL.Data;
using PermitQL.Models;
using NSubstitute;

public sealed class AdoNetDataAccessorMetadataTests
{
    [Fact]
    public async Task GetTableConstraintsAsync_DelegatesToConstraintResolver()
    {
        var connection = Substitute.For<DbConnection>();
        connection.OpenAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var resolver = Substitute.For<IConstraintResolver>();
        resolver.ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));

        var accessor = new AdoNetDataAccessor(
            () => connection,
            constraintResolver: resolver);

        await accessor.GetTableConstraintsAsync("public", "orders");

        await resolver.Received(1).ResolveAsync(connection, "public", "orders", Arg.Any<CancellationToken>());
    }
}
