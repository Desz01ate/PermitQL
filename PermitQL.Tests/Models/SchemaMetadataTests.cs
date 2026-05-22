namespace PermitQL.Tests.Models;

using PermitQL.Models;

public sealed class SchemaMetadataTests
{
    [Fact]
    public void ColumnMetadata_CanRepresentDefaultAndGenerationState()
    {
        var column = new SchemaColumnMetadata(
            Name: "id",
            Type: "integer",
            IsNullable: false,
            IsPrimaryKey: true,
            DefaultValue: null,
            IsGenerated: true,
            GenerationKind: GenerationKind.Identity);

        Assert.True(column.IsPrimaryKey);
        Assert.True(column.IsGenerated);
        Assert.Equal(GenerationKind.Identity, column.GenerationKind);
    }

    [Fact]
    public void TableStatistics_UsesNullableApproximateRowCount()
    {
        var stats = new TableStatisticsMetadata(ApproximateRowCount: null, LastAnalyzed: null);

        Assert.Null(stats.ApproximateRowCount);
        Assert.Null(stats.LastAnalyzed);
    }
}
