namespace PermitQL.Tests.Server;

using System.Text.Json;
using PermitQL.Abstractions;
using PermitQL.Models;
using PermitQL.Server;
using PermitQL.Server.Tools;
using NSubstitute;

public sealed class DescribeDatabaseToolTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDataAccessor _dataAccessor = Substitute.For<IDataAccessor>();
    private readonly IRulesProvider _rulesProvider = Substitute.For<IRulesProvider>();
    private readonly IPermitQLFactory _factory = Substitute.For<IPermitQLFactory>();
    private readonly ValidatorCapabilityDescriptor _validatorCapabilities = new();

    private RuleSet MakeRules(
        Dictionary<string, SchemaRule>? schemas = null,
        int maxRows = 100,
        int timeoutMs = 5000,
        string[]? globalOps = null)
    {
        return new RuleSet
        {
            Version = "1",
            Database = "test_db",
            GlobalLimits = new GlobalLimits
            {
                MaxRowsReturned = maxRows,
                TimeoutMs = timeoutMs,
                AllowedOperations = globalOps ?? ["select"],
            },
            ExposedSchemas = schemas ?? new Dictionary<string, SchemaRule>(),
        };
    }

    private void SetupEmptyMetadata(string schema, string table)
    {
        this._dataAccessor.GetTableColumnsAsync(schema, table, Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>());
        this._dataAccessor.GetTableConstraintsAsync(schema, table, Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync(schema, table, Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync(schema, table, Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync(schema, table, Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync(schema, table, Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(null, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported,
                CapabilitySupport.Supported,
                CapabilitySupport.Supported,
                CapabilitySupport.Supported,
                CapabilitySupport.Supported,
                []));
    }

    private async Task<JsonElement> CallDescribeDatabase(string key = "test")
    {
        var json = await PermitQLTools.DescribeDatabase(this._dataAccessor, this._rulesProvider, this._factory, this._validatorCapabilities, key);
        return JsonDocument.Parse(json).RootElement;
    }

    // ==================== Top-level shape ====================

    [Fact]
    public async Task ReturnsValidJson_WithExpectedTopLevelKeys()
    {
        var rules = this.MakeRules();
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();

        Assert.True(root.TryGetProperty("database", out _));
        Assert.True(root.TryGetProperty("limits", out _));
        Assert.True(root.TryGetProperty("capabilities", out _));
        Assert.True(root.TryGetProperty("schemas", out _));
    }

    [Fact]
    public async Task DatabaseSection_ContainsRuleSetKeyAndDialect()
    {
        var rules = this.MakeRules();
        this._rulesProvider.GetRuleSet("mykey").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase("mykey");
        var db = root.GetProperty("database");

        Assert.Equal("mykey", db.GetProperty("ruleSetKey").GetString());
        Assert.Equal("PostgreSQL", db.GetProperty("dialect").GetString());
    }

    [Fact]
    public async Task LimitsSection_ReflectsGlobalLimits()
    {
        var rules = this.MakeRules(maxRows: 500, timeoutMs: 10000);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.Sqlite);
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var limits = root.GetProperty("limits");

        Assert.Equal(500, limits.GetProperty("maxRowsReturned").GetInt32());
        Assert.Equal(10000, limits.GetProperty("timeoutMs").GetInt32());
    }

    // ==================== Capabilities merging ====================

    [Fact]
    public async Task Capabilities_ValidatorOverridesProvider_ForNonUnknownValues()
    {
        var rules = this.MakeRules();
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        // Provider says CTEs supported, but validator says unsupported => validator wins
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                Ctes: CapabilitySupport.Supported,
                Subqueries: CapabilitySupport.Supported,
                DerivedTables: CapabilitySupport.Supported,
                WindowFunctions: CapabilitySupport.Supported,
                Mutations: CapabilitySupport.Supported,
                Notes: ["provider note"]));

        // ValidatorCapabilityDescriptor returns: CTEs=Unsupported, Subqueries=Supported,
        // DerivedTables=Supported, WindowFunctions=Unknown, Mutations=Supported
        var root = await this.CallDescribeDatabase();
        var caps = root.GetProperty("capabilities");

        Assert.Equal("unsupported", caps.GetProperty("ctes").GetString());
        Assert.Equal("supported", caps.GetProperty("subqueries").GetString());
        Assert.Equal("supported", caps.GetProperty("derivedTables").GetString());
        // WindowFunctions=Unknown from validator => provider's Supported wins
        Assert.Equal("supported", caps.GetProperty("windowFunctions").GetString());
        Assert.Equal("supported", caps.GetProperty("mutations").GetString());
    }

    [Fact]
    public async Task Capabilities_NotesAreMerged()
    {
        var rules = this.MakeRules();
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported,
                ["provider note"]));

        var root = await this.CallDescribeDatabase();
        var notes = root.GetProperty("capabilities").GetProperty("notes");

        Assert.Equal(JsonValueKind.Array, notes.ValueKind);
        // Should contain both provider and validator notes
        var notesList = notes.EnumerateArray().Select(n => n.GetString()).ToList();
        Assert.Contains("provider note", notesList);
        Assert.True(notesList.Count >= 2); // at least validator note too
    }

    // ==================== Column filtering ====================

    [Fact]
    public async Task Columns_HiddenColumnsAreExcluded()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["users"] = new TableRule
                    {
                        AllowedColumns = ["id", "name"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("name", "text", false, false, null, false, GenerationKind.None),
                new("secret", "text", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(1000, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("users");
        var columns = table.GetProperty("columns");

        var colNames = columns.EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("id", colNames);
        Assert.Contains("name", colNames);
        Assert.DoesNotContain("secret", colNames);
    }

    [Fact]
    public async Task Columns_WildcardWithDenied_ExcludesDeniedColumns()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["users"] = new TableRule
                    {
                        AllowedColumns = ["*"],
                        DeniedColumns = ["secret"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("name", "text", false, false, null, false, GenerationKind.None),
                new("secret", "text", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(1000, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var columns = root.GetProperty("schemas").GetProperty("public")
                          .GetProperty("tables").GetProperty("users")
                          .GetProperty("columns");
        var colNames = columns.EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("id", colNames);
        Assert.Contains("name", colNames);
        Assert.DoesNotContain("secret", colNames);
    }

    // ==================== Constraint filtering ====================

    [Fact]
    public async Task Constraints_ReferencingHiddenColumn_AreDropped_WithOmission()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id", "total"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("total", "numeric", false, false, null, false, GenerationKind.None),
                new("secret_code", "text", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata(
                [
                    new UniqueConstraintMetadata("uq_id", ["id"]),
                    new UniqueConstraintMetadata("uq_secret", ["secret_code"]),
                ],
                [
                    new CheckConstraintMetadata("chk_total", "total > 0"),
                    new CheckConstraintMetadata("chk_secret", "secret_code IS NOT NULL"),
                ]));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(1000, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("orders");

        // Only uq_id should remain (uq_secret references hidden column)
        var unique = table.GetProperty("constraints").GetProperty("unique");
        Assert.Equal(1, unique.GetArrayLength());
        Assert.Equal("uq_id", unique[0].GetProperty("name").GetString());

        // chk_secret references hidden column expression — but check constraints
        // are filtered by column reference in expression. Since we can't reliably parse
        // the expression, we keep all checks. Actually the spec says constraints referencing
        // hidden columns should be dropped. For check constraints, we check if expression
        // mentions any hidden column name.
        var check = table.GetProperty("constraints").GetProperty("check");
        Assert.Equal(1, check.GetArrayLength());
        Assert.Equal("chk_total", check[0].GetProperty("name").GetString());

        var omissions = table.GetProperty("omissions");
        var omissionsList = omissions.EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.Contains("hidden_constraints_omitted", omissionsList);
    }

    // ==================== Index filtering ====================

    [Fact]
    public async Task Indexes_ReferencingHiddenColumn_AreDropped_WithOmission()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id", "total"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("total", "numeric", false, false, null, false, GenerationKind.None),
                new("hidden_col", "text", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>
            {
                new("idx_id", ["id"], true),
                new("idx_hidden", ["hidden_col"], false),
                new("idx_composite", ["id", "hidden_col"], false),
            });
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(1000, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("orders");

        var indexes = table.GetProperty("indexes");
        Assert.Equal(1, indexes.GetArrayLength());
        Assert.Equal("idx_id", indexes[0].GetProperty("name").GetString());

        var omissions = table.GetProperty("omissions");
        var omissionsList = omissions.EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.Contains("hidden_indexes_omitted", omissionsList);
    }

    // ==================== Foreign key filtering ====================

    [Fact]
    public async Task OutboundFks_OnlyIncluded_WhenFromColumnAllowed_AndTargetExposed()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id", "customer_id"],
                        RowFilter = null,
                    },
                    ["customers"] = new TableRule
                    {
                        AllowedColumns = ["id", "name"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("customer_id", "integer", false, false, null, false, GenerationKind.None),
                new("hidden_ref", "integer", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>
            {
                // Allowed: from allowed column to exposed table
                new("fk_customer", "public", "orders", "customer_id", "public", "customers", "id", null, null),
                // Filtered: from hidden column
                new("fk_hidden", "public", "orders", "hidden_ref", "public", "customers", "id", null, null),
                // Filtered: target not exposed
                new("fk_external", "public", "orders", "customer_id", "private", "secret_table", "id", null, null),
            });

        // Setup remaining metadata for "orders"
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(1000, null));

        // Setup metadata for "customers"
        this.SetupEmptyMetadata("public", "customers");

        var root = await this.CallDescribeDatabase();
        var ordersTable = root.GetProperty("schemas").GetProperty("public")
                              .GetProperty("tables").GetProperty("orders");
        var outbound = ordersTable.GetProperty("relationships").GetProperty("outbound");

        Assert.Equal(1, outbound.GetArrayLength());
        Assert.Equal("fk_customer", outbound[0].GetProperty("constraintName").GetString());
    }

    [Fact]
    public async Task InboundFks_OnlyIncluded_WhenSourceExposed_AndSourceColumnAllowed()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["customers"] = new TableRule
                    {
                        AllowedColumns = ["id", "name"],
                        RowFilter = null,
                    },
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id", "customer_id"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        // Setup "customers" metadata
        this._dataAccessor.GetTableColumnsAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("name", "text", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>
            {
                // Allowed: source table exposed, source column allowed
                new("fk_customer", "public", "orders", "customer_id", "public", "customers", "id", null, null),
                // Filtered: source table not exposed
                new("fk_secret", "secret", "hidden_table", "customer_id", "public", "customers", "id", null, null),
            });
        this._dataAccessor.GetTableIndexesAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(500, null));

        // Setup "orders" metadata
        this.SetupEmptyMetadata("public", "orders");

        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var customersTable = root.GetProperty("schemas").GetProperty("public")
                                 .GetProperty("tables").GetProperty("customers");
        var inbound = customersTable.GetProperty("relationships").GetProperty("inbound");

        Assert.Equal(1, inbound.GetArrayLength());
        Assert.Equal("fk_customer", inbound[0].GetProperty("constraintName").GetString());
    }

    [Fact]
    public async Task FilteredFks_AddHiddenRelationshipsOmitted()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id", "customer_id"],
                        RowFilter = null,
                    },
                    ["customers"] = new TableRule
                    {
                        AllowedColumns = ["id", "name"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("customer_id", "integer", false, false, null, false, GenerationKind.None),
                new("hidden_ref", "integer", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>
            {
                // Allowed FK
                new("fk_customer", "public", "orders", "customer_id", "public", "customers", "id", null, null),
                // Filtered: from hidden column
                new("fk_hidden", "public", "orders", "hidden_ref", "public", "customers", "id", null, null),
            });
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(100, null));

        this.SetupEmptyMetadata("public", "customers");

        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var ordersTable = root.GetProperty("schemas").GetProperty("public")
                              .GetProperty("tables").GetProperty("orders");

        var omissions = ordersTable.GetProperty("omissions");
        var omissionsList = omissions.EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.Contains("hidden_relationships_omitted", omissionsList);

        // Only one "hidden_relationships_omitted" entry even when both outbound and inbound are filtered
        Assert.Single(omissionsList, o => o == "hidden_relationships_omitted");
    }

    [Fact]
    public async Task FilteredInboundFks_AddHiddenRelationshipsOmitted()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["customers"] = new TableRule
                    {
                        AllowedColumns = ["id", "name"],
                        RowFilter = null,
                    },
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id", "customer_id"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("name", "text", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>
            {
                // Filtered: source schema/table not exposed
                new("fk_from_hidden", "hidden_schema", "secret_table", "customer_id", "public", "customers", "id", null, null),
            });
        this._dataAccessor.GetTableIndexesAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "customers", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(500, null));

        this.SetupEmptyMetadata("public", "orders");

        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var customersTable = root.GetProperty("schemas").GetProperty("public")
                                 .GetProperty("tables").GetProperty("customers");

        var omissions = customersTable.GetProperty("omissions");
        var omissionsList = omissions.EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.Contains("hidden_relationships_omitted", omissionsList);
    }

    // ==================== Statistics and omissions ====================

    [Fact]
    public async Task Statistics_NullRowCount_AddsUnavailableStatisticsOmission()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(null, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("orders");

        var omissions = table.GetProperty("omissions");
        var omissionsList = omissions.EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.Contains("unavailable_statistics", omissionsList);
    }

    [Fact]
    public async Task Statistics_WithRowCount_NoUnavailableOmission()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(5000, DateTimeOffset.UtcNow));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("orders");

        var omissions = table.GetProperty("omissions");
        var omissionsList = omissions.EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.DoesNotContain("unavailable_statistics", omissionsList);

        var stats = table.GetProperty("statistics");
        Assert.Equal(5000, stats.GetProperty("approximateRowCount").GetInt64());
    }

    // ==================== Table structure ====================

    [Fact]
    public async Task TableEntry_ContainsAllExpectedFields()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id", "total"],
                        AllowedOperations = ["select", "insert"],
                        RowFilter = "status = 'active'",
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("total", "numeric", false, false, "0", false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(null, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("orders");

        // allowedOperations
        var ops = table.GetProperty("allowedOperations").EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.Contains("select", ops);
        Assert.Contains("insert", ops);

        // rowFilter
        Assert.Equal("status = 'active'", table.GetProperty("rowFilter").GetString());

        // columns, constraints, relationships, indexes, statistics, omissions exist
        Assert.True(table.TryGetProperty("columns", out _));
        Assert.True(table.TryGetProperty("constraints", out _));
        Assert.True(table.TryGetProperty("relationships", out _));
        Assert.True(table.TryGetProperty("indexes", out _));
        Assert.True(table.TryGetProperty("statistics", out _));
        Assert.True(table.TryGetProperty("omissions", out _));
    }

    // ==================== Unsafe identifier skipping ====================

    [Fact]
    public async Task UnsafeSchemaNames_AreSkipped()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule { AllowedColumns = ["id"] },
                },
            },
            ["Robert'; DROP TABLE Students;--"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["evil"] = new TableRule { AllowedColumns = ["id"] },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this.SetupEmptyMetadata("public", "orders");
        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
            });

        var root = await this.CallDescribeDatabase();
        var schemas_ = root.GetProperty("schemas");

        Assert.True(schemas_.TryGetProperty("public", out _));
        Assert.False(schemas_.TryGetProperty("Robert'; DROP TABLE Students;--", out _));
    }

    // ==================== Row filter null ====================

    [Fact]
    public async Task RowFilter_Null_SerializedAsJsonNull()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(null, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("orders");

        Assert.Equal(JsonValueKind.Null, table.GetProperty("rowFilter").ValueKind);
    }

    // ==================== Dialect formatting ====================

    [Theory]
    [InlineData(SqlDialect.PostgreSql, "PostgreSQL")]
    [InlineData(SqlDialect.Sqlite, "SQLite")]
    [InlineData(SqlDialect.SqlServer, "SQL Server")]
    public async Task Dialect_FormattedCorrectly(SqlDialect dialect, string expected)
    {
        var rules = this.MakeRules();
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(dialect);
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        Assert.Equal(expected, root.GetProperty("database").GetProperty("dialect").GetString());
    }

    // ==================== AllowedOperations fallback ====================

    // ==================== Absence semantics ====================

    [Fact]
    public async Task AbsenceSemantics_EmptyIndexes_NullStatistics_UnavailableOmission()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["events"] = new TableRule
                    {
                        AllowedColumns = ["id", "name"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "events", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("name", "text", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "events", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "events", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "events", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        // No indexes at all
        this._dataAccessor.GetTableIndexesAsync("public", "events", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        // Statistics with null row count
        this._dataAccessor.GetTableStatisticsAsync("public", "events", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(null, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("events");

        // 1. Empty indexes => empty JSON array, not null or missing
        var indexes = table.GetProperty("indexes");
        Assert.Equal(JsonValueKind.Array, indexes.ValueKind);
        Assert.Equal(0, indexes.GetArrayLength());

        // 2. Null ApproximateRowCount => JSON null for approximateRowCount
        var stats = table.GetProperty("statistics");
        var rowCountProp = stats.GetProperty("approximateRowCount");
        Assert.Equal(JsonValueKind.Null, rowCountProp.ValueKind);

        // 3. unavailable_statistics appears in omissions
        var omissions = table.GetProperty("omissions");
        var omissionsList = omissions.EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.Contains("unavailable_statistics", omissionsList);
    }

    // ==================== AllowedOperations fallback ====================

    [Fact]
    public async Task AllowedOperations_FallsBackToGlobalLimits_WhenTableDoesNotSpecify()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id"],
                        AllowedOperations = null, // no table-level override
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas, globalOps: ["select", "insert"]);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(null, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var ops = root.GetProperty("schemas").GetProperty("public")
                      .GetProperty("tables").GetProperty("orders")
                      .GetProperty("allowedOperations")
                      .EnumerateArray().Select(o => o.GetString()).ToList();

        Assert.Contains("select", ops);
        Assert.Contains("insert", ops);
    }

    // ==================== Column statistics ====================

    [Fact]
    public async Task ColumnStatistics_IncludedInStatisticsSection()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["employees"] = new TableRule
                    {
                        AllowedColumns = ["id", "salary"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "employees", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("salary", "numeric", true, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "employees", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "employees", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "employees", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "employees", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "employees", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(24, null,
                new Dictionary<string, ColumnStatisticsMetadata>
                {
                    ["id"] = new(0.0, 24, null, null, "100", "206"),
                    ["salary"] = new(0.04, 20, ["2400", "4200"], [0.2, 0.15], "2100", "24000"),
                }));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var stats = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("employees")
                        .GetProperty("statistics");

        Assert.Equal(24, stats.GetProperty("approximateRowCount").GetInt64());

        var colStats = stats.GetProperty("columnStatistics");
        Assert.Equal(JsonValueKind.Object, colStats.ValueKind);

        var salaryStats = colStats.GetProperty("salary");
        Assert.Equal(0.04, salaryStats.GetProperty("nullFraction").GetDouble(), 0.001);
        Assert.Equal(20, salaryStats.GetProperty("approximateDistinctCount").GetInt64());
        Assert.Equal("2100", salaryStats.GetProperty("minValue").GetString());
        Assert.Equal("24000", salaryStats.GetProperty("maxValue").GetString());

        var mcv = salaryStats.GetProperty("mostCommonValues");
        Assert.Equal(2, mcv.GetArrayLength());
        Assert.Equal("2400", mcv[0].GetString());
    }

    [Fact]
    public async Task ColumnStatistics_HiddenColumnsFiltered()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["users"] = new TableRule
                    {
                        AllowedColumns = ["id", "name"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
                new("name", "text", false, false, null, false, GenerationKind.None),
                new("secret", "text", false, false, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "users", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(100, null,
                new Dictionary<string, ColumnStatisticsMetadata>
                {
                    ["id"] = new(0.0, 100, null, null, "1", "100"),
                    ["name"] = new(0.0, 90, null, null, null, null),
                    ["secret"] = new(0.0, 50, ["password123"], [0.5], null, null),
                }));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var table = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("users");

        var colStats = table.GetProperty("statistics").GetProperty("columnStatistics");
        var colNames = colStats.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("id", colNames);
        Assert.Contains("name", colNames);
        Assert.DoesNotContain("secret", colNames);

        var omissions = table.GetProperty("omissions");
        var omissionsList = omissions.EnumerateArray()
            .Select(o => o.GetString()).ToList();
        Assert.Contains("hidden_column_statistics_omitted", omissionsList);
    }

    [Fact]
    public async Task ColumnStatistics_NullWhenNoColumnStats()
    {
        var schemas = new Dictionary<string, SchemaRule>
        {
            ["public"] = new SchemaRule
            {
                Tables = new Dictionary<string, TableRule>
                {
                    ["orders"] = new TableRule
                    {
                        AllowedColumns = ["id"],
                        RowFilter = null,
                    },
                },
            },
        };
        var rules = this.MakeRules(schemas);
        this._rulesProvider.GetRuleSet("test").Returns(rules);
        this._factory.Dialect.Returns(SqlDialect.PostgreSql);

        this._dataAccessor.GetTableColumnsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<SchemaColumnMetadata>
            {
                new("id", "integer", false, true, null, false, GenerationKind.None),
            });
        this._dataAccessor.GetTableConstraintsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableConstraintMetadata([], []));
        this._dataAccessor.GetOutboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetInboundForeignKeysAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<ForeignKeyMetadata>());
        this._dataAccessor.GetTableIndexesAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new List<TableIndexMetadata>());
        this._dataAccessor.GetTableStatisticsAsync("public", "orders", Arg.Any<CancellationToken>())
            .Returns(new TableStatisticsMetadata(500, null));
        this._dataAccessor.GetQueryCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new QueryCapabilityMetadata(
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, CapabilitySupport.Supported,
                CapabilitySupport.Supported, []));

        var root = await this.CallDescribeDatabase();
        var stats = root.GetProperty("schemas").GetProperty("public")
                        .GetProperty("tables").GetProperty("orders")
                        .GetProperty("statistics");

        Assert.Equal(JsonValueKind.Null, stats.GetProperty("columnStatistics").ValueKind);
    }
}
