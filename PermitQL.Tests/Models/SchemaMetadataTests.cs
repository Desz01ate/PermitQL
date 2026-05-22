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

    [Fact]
    public void TableStatistics_BackwardCompatible_WithTwoArgConstruction()
    {
        var stats = new TableStatisticsMetadata(100, null);

        Assert.Equal(100, stats.ApproximateRowCount);
        Assert.Null(stats.ColumnStatistics);
    }

    [Fact]
    public void TableStatistics_WithColumnStatistics()
    {
        var colStats = new Dictionary<string, ColumnStatisticsMetadata>
        {
            ["salary"] = new(0.04, 20, ["2400"], [0.2], "2100", "24000"),
        };

        var stats = new TableStatisticsMetadata(100, null, colStats);

        Assert.NotNull(stats.ColumnStatistics);
        Assert.True(stats.ColumnStatistics.ContainsKey("salary"));
        Assert.Equal(0.04, stats.ColumnStatistics["salary"].NullFraction);
    }

    [Fact]
    public void ColumnStatisticsMetadata_CanRepresentFullAndPartialData()
    {
        var full = new ColumnStatisticsMetadata(0.05, 42, ["a", "b"], [0.6, 0.4], "1", "100");
        Assert.Equal(0.05, full.NullFraction);
        Assert.Equal(42, full.ApproximateDistinctCount);
        Assert.Equal(2, full.MostCommonValues!.Count);
        Assert.Equal("1", full.MinValue);

        var partial = new ColumnStatisticsMetadata(null, null, null, null, null, null);
        Assert.Null(partial.NullFraction);
        Assert.Null(partial.MostCommonValues);
    }
}
