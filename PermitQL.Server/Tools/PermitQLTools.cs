namespace PermitQL.Server.Tools;

using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PermitQL.Abstractions;
using PermitQL.Models;
using ModelContextProtocol.Server;

[McpServerToolType]
public static partial class PermitQLTools
{
    private readonly static JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex SafeIdentifierPattern();

    [McpServerTool(Name = "query"),
     Description(
         "Execute a SQL query against a governed database. The query is validated against access rules and rewritten to enforce row filters and limits before execution.")]
    public static async Task<string> Query(
        IQueryPipeline pipeline,
        [Description("The SQL SELECT query to execute")]
        string query,
        [Description("The rule set key identifying which database and access rules to use")]
        string ruleSetKey,
        [Description("Response format: 'markdown' for a readable table or 'json' for structured data")]
        string format = "json",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await pipeline.ExecuteAsync(query, ruleSetKey, cancellationToken);

            return format.Equals("json", StringComparison.OrdinalIgnoreCase)
                ? FormatAsJson(result)
                : FormatAsMarkdown(result);
        }
        catch (Exception ex)
        {
            var (message, type, _) = ErrorHandler.Classify(ex);
            return $"Error ({type}): {message}";
        }
    }

    [McpServerTool(Name = "list_databases"),
     Description(
         "List available database rule set keys that can be used with the query and describe_database tools.")]
    public static string ListDatabases(IRulesProvider rulesProvider)
    {
        var keys = rulesProvider.GetAvailableKeys();
        return JsonSerializer.Serialize(keys, JsonOptions);
    }

    [McpServerTool(Name = "describe_database"),
     Description(
         "Describe the structure of a governed database. Returns the SQL dialect, limits, capabilities, schemas, tables, columns, constraints, relationships, indexes, and statistics that are accessible under the specified rule set.")]
    public static async Task<string> DescribeDatabase(
        IDataAccessor dataAccessor,
        IRulesProvider rulesProvider,
        IPermitQLFactory factory,
        ValidatorCapabilityDescriptor validatorCapabilities,
        [Description("The rule set key identifying which database to describe")]
        string ruleSetKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rules = rulesProvider.GetRuleSet(ruleSetKey);

            // Build capabilities by merging provider + validator (validator wins for non-Unknown)
            var providerCaps = await dataAccessor.GetQueryCapabilitiesAsync(cancellationToken);
            var validatorCaps = validatorCapabilities.Describe();
            var mergedCaps = MergeCapabilities(providerCaps, validatorCaps);

            // Build schemas
            var schemasObj = new Dictionary<string, object>();

            foreach (var (schema, schemaRule) in rules.ExposedSchemas)
            {
                if (!SafeIdentifierPattern().IsMatch(schema))
                    continue;

                var tablesObj = new Dictionary<string, object>();

                foreach (var (table, tableRule) in schemaRule.Tables)
                {
                    if (!SafeIdentifierPattern().IsMatch(table))
                        continue;

                    tablesObj[table] = await BuildTableDescription(
                        dataAccessor, rules, schema, table, tableRule, cancellationToken);
                }

                schemasObj[schema] = new { tables = tablesObj };
            }

            var response = new
            {
                database = new
                {
                    ruleSetKey,
                    dialect = FormatDialect(factory.Dialect),
                },
                limits = new
                {
                    maxRowsReturned = rules.GlobalLimits.MaxRowsReturned,
                    timeoutMs = rules.GlobalLimits.TimeoutMs,
                },
                capabilities = new
                {
                    ctes = FormatCapability(mergedCaps.Ctes),
                    subqueries = FormatCapability(mergedCaps.Subqueries),
                    derivedTables = FormatCapability(mergedCaps.DerivedTables),
                    windowFunctions = FormatCapability(mergedCaps.WindowFunctions),
                    mutations = FormatCapability(mergedCaps.Mutations),
                    notes = mergedCaps.Notes,
                },
                schemas = schemasObj,
            };

            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            var (message, type, _) = ErrorHandler.Classify(ex);
            return $"Error ({type}): {message}";
        }
    }

    private static async Task<object> BuildTableDescription(
        IDataAccessor dataAccessor,
        RuleSet rules,
        string schema,
        string table,
        TableRule tableRule,
        CancellationToken cancellationToken)
    {
        var allColumns = await dataAccessor.GetTableColumnsAsync(schema, table, cancellationToken);
        var constraints = await dataAccessor.GetTableConstraintsAsync(schema, table, cancellationToken);
        var outboundFks = await dataAccessor.GetOutboundForeignKeysAsync(schema, table, cancellationToken);
        var inboundFks = await dataAccessor.GetInboundForeignKeysAsync(schema, table, cancellationToken);
        var indexes = await dataAccessor.GetTableIndexesAsync(schema, table, cancellationToken);
        var statistics = await dataAccessor.GetTableStatisticsAsync(schema, table, cancellationToken);

        var allowedOps = tableRule.AllowedOperations ?? rules.GlobalLimits.AllowedOperations;
        var omissions = new List<string>();

        // Filter columns: only allowed columns
        var visibleColumns = allColumns.Where(c => tableRule.IsColumnAllowed(c.Name)).ToList();
        var hiddenColumnNames = new HashSet<string>(
            allColumns.Where(c => !tableRule.IsColumnAllowed(c.Name)).Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        // Filter constraints
        var filteredUnique = constraints.Unique
                                        .Where(u => !u.Columns.Any(c => hiddenColumnNames.Contains(c)))
                                        .ToList();
        var filteredCheck = constraints.Check
                                       .Where(ch => !hiddenColumnNames.Any(hc =>
                                           ch.Expression.Contains(hc, StringComparison.OrdinalIgnoreCase)))
                                       .ToList();

        if (filteredUnique.Count < constraints.Unique.Count ||
            filteredCheck.Count < constraints.Check.Count)
        {
            omissions.Add("hidden_constraints_omitted");
        }

        // Filter indexes
        var filteredIndexes = indexes
                              .Where(ix => !ix.Columns.Any(c => hiddenColumnNames.Contains(c)))
                              .ToList();

        if (filteredIndexes.Count < indexes.Count)
        {
            omissions.Add("hidden_indexes_omitted");
        }

        // Filter outbound FKs: FromColumn allowed AND target table/schema is exposed
        var visibleOutbound = outboundFks.Where(fk =>
            tableRule.IsColumnAllowed(fk.FromColumn) &&
            rules.ExposedSchemas.TryGetValue(fk.ToSchema, out var refSchemaRule) &&
            refSchemaRule.Tables.ContainsKey(fk.ToTable)).ToList();

        // Filter inbound FKs: source table/schema is exposed AND source column is allowed
        var visibleInbound = inboundFks.Where(fk =>
            rules.ExposedSchemas.TryGetValue(fk.FromSchema, out var srcSchemaRule) &&
            srcSchemaRule.Tables.TryGetValue(fk.FromTable, out var srcTableRule) &&
            srcTableRule.IsColumnAllowed(fk.FromColumn)).ToList();

        if (outboundFks.Count != visibleOutbound.Count || inboundFks.Count != visibleInbound.Count)
        {
            omissions.Add("hidden_relationships_omitted");
        }

        // Statistics omission
        if (statistics.ApproximateRowCount is null)
        {
            omissions.Add("unavailable_statistics");
        }

        // Filter column statistics to only visible columns
        Dictionary<string, ColumnStatisticsMetadata>? filteredColumnStats = null;

        if (statistics.ColumnStatistics is { } allColumnStats)
        {
            filteredColumnStats = allColumnStats
                                  .Where(kvp => !hiddenColumnNames.Contains(kvp.Key))
                                  .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            if (filteredColumnStats.Count < allColumnStats.Count)
                omissions.Add("hidden_column_statistics_omitted");

            if (filteredColumnStats.Count == 0)
                filteredColumnStats = null;
        }

        return new
        {
            allowedOperations = allowedOps,
            rowFilter = tableRule.RowFilter,
            columns = visibleColumns.Select(c => new
            {
                c.Name,
                c.Type,
                c.IsNullable,
                c.IsPrimaryKey,
                c.DefaultValue,
                c.IsGenerated,
                generationKind = FormatGenerationKind(c.GenerationKind),
                semanticDescription = tableRule.ColumnSemanticDescriptions.GetValueOrDefault(c.Name),
            }),
            constraints = new
            {
                unique = filteredUnique.Select(u => new
                {
                    u.Name,
                    u.Columns,
                }),
                check = filteredCheck.Select(ch => new
                {
                    ch.Name,
                    ch.Expression,
                }),
            },
            relationships = new
            {
                outbound = visibleOutbound.Select(fk => new
                {
                    fk.ConstraintName,
                    fk.FromSchema,
                    fk.FromTable,
                    fk.FromColumn,
                    fk.ToSchema,
                    fk.ToTable,
                    fk.ToColumn,
                    fk.OnDelete,
                    fk.OnUpdate,
                }),
                inbound = visibleInbound.Select(fk => new
                {
                    fk.ConstraintName,
                    fk.FromSchema,
                    fk.FromTable,
                    fk.FromColumn,
                    fk.ToSchema,
                    fk.ToTable,
                    fk.ToColumn,
                    fk.OnDelete,
                    fk.OnUpdate,
                }),
            },
            indexes = filteredIndexes.Select(ix => new
            {
                ix.Name,
                ix.Columns,
                ix.IsUnique,
            }),
            statistics = new
            {
                statistics.ApproximateRowCount,
                statistics.LastAnalyzed,
                columnStatistics = filteredColumnStats?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        kvp.Value.NullFraction,
                        kvp.Value.ApproximateDistinctCount,
                        kvp.Value.MostCommonValues,
                        kvp.Value.MostCommonFrequencies,
                        kvp.Value.MinValue,
                        kvp.Value.MaxValue,
                    }),
            },
            tableSemanticDescription = tableRule.TableSemanticDescription,
            omissions,
        };
    }

    private static QueryCapabilityMetadata MergeCapabilities(
        QueryCapabilityMetadata provider,
        QueryCapabilityMetadata validator)
    {
        return new QueryCapabilityMetadata(
            Ctes: MergeCapability(provider.Ctes, validator.Ctes),
            Subqueries: MergeCapability(provider.Subqueries, validator.Subqueries),
            DerivedTables: MergeCapability(provider.DerivedTables, validator.DerivedTables),
            WindowFunctions: MergeCapability(provider.WindowFunctions, validator.WindowFunctions),
            Mutations: MergeCapability(provider.Mutations, validator.Mutations),
            Notes: provider.Notes.Concat(validator.Notes).ToList());
    }

    private static CapabilitySupport MergeCapability(
        CapabilitySupport provider,
        CapabilitySupport validator)
    {
        // Validator wins for non-Unknown values
        return validator != CapabilitySupport.Unknown ? validator : provider;
    }

    private static string FormatCapability(CapabilitySupport support) => support switch
    {
        CapabilitySupport.Supported => "supported",
        CapabilitySupport.Unsupported => "unsupported",
        CapabilitySupport.Unknown => "unknown",
        _ => support.ToString().ToLowerInvariant(),
    };

    private static string FormatGenerationKind(GenerationKind kind) => kind switch
    {
        GenerationKind.None => "none",
        GenerationKind.Identity => "identity",
        GenerationKind.AutoIncrement => "autoIncrement",
        GenerationKind.Computed => "computed",
        GenerationKind.Unknown => "unknown",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static string FormatDialect(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => "PostgreSQL",
        SqlDialect.Sqlite => "SQLite",
        SqlDialect.SqlServer => "SQL Server",
        _ => dialect.ToString(),
    };

    private static string FormatAsMarkdown(PermitQL.Models.QueryResult result)
    {
        var sb = new StringBuilder();

        // Header row
        sb.Append('|');

        foreach (var col in result.Columns)
        {
            sb.Append($" {col.Name} |");
        }

        sb.AppendLine();

        // Separator row
        sb.Append('|');

        foreach (var _ in result.Columns)
        {
            sb.Append(" --- |");
        }

        sb.AppendLine();

        // Data rows
        foreach (var row in result.Rows)
        {
            sb.Append('|');

            foreach (var cell in row)
            {
                var value = cell switch
                {
                    null => "NULL",
                    string s => s,
                    System.Collections.IEnumerable items => $"[{string.Join(", ", items.Cast<object>())}]",
                    _ => cell.ToString(),
                };
                sb.Append($" {value} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.Append($"{result.Rows.Count} row(s) returned.");

        return sb.ToString();
    }

    private static string FormatAsJson(PermitQL.Models.QueryResult result)
    {
        var response = new
        {
            columns = result.Columns.Select(c => new { c.Name, c.Type, c.IsNullable }),
            rows = result.Rows,
            rowCount = result.Rows.Count,
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }
}