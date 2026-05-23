namespace PermitQL.Validation;

using Abstractions;
using Exceptions;
using Models;
using SqlParser.Ast;

public sealed class QueryValidator : IQueryValidator
{
    public ValueTask<ValidationResult> ValidateAsync(
        ParsedQuery query,
        RuleSet rules,
        CancellationToken cancellationToken = default)
    {
        // 1. Reject unsupported statement types (DDL, etc.)
        if (query.StatementType == StatementKind.Other)
        {
            return new ValueTask<ValidationResult>(new ValidationResult(
                ValidationResultType.Invalid,
                "Statement type is not supported. Only SELECT, INSERT, UPDATE, and DELETE are allowed."));
        }

        // 1b. Reject tableless SELECT queries (e.g. SELECT pg_sleep(10))
        if (query is { StatementType: StatementKind.Select, ReferencedTables.Count: 0 })
        {
            return new ValueTask<ValidationResult>(new ValidationResult(
                ValidationResultType.Invalid,
                "Tableless queries are not allowed. Direct function calls are not permitted."));
        }

        // 2. Table access check — also builds alias map for column validation
        var aliasMap = BuildAliasMap(query);
        var (resolvedTables, tableCheck) = ValidateTableAccess(query, rules);
        if (tableCheck is not null)
            return new ValueTask<ValidationResult>(tableCheck);

        // 3. CTE column access check
        if (query.CteDefinitions is { } cteDefs)
        {
            var cteColumnCheck = ValidateCteColumnAccess(cteDefs, resolvedTables);
            if (cteColumnCheck is not null)
                return new ValueTask<ValidationResult>(cteColumnCheck);
        }

        // 4. Mutation permission check (for DML only)
        if (query.StatementType != StatementKind.Select)
        {
            var mutationCheck = ValidateMutationPermissions(query, rules, resolvedTables);
            if (mutationCheck is not null)
                return new ValueTask<ValidationResult>(mutationCheck);
        }

        // 5. Column access check
        if (query.ReferencedColumns.Count > 0)
        {
            var columnCheck = ValidateColumnAccess(query, rules, resolvedTables, aliasMap);
            if (columnCheck is not null)
                return new ValueTask<ValidationResult>(columnCheck);
        }

        return new ValueTask<ValidationResult>(
            new ValidationResult(ValidationResultType.Valid, null));
    }

    private static ValidationResult? ValidateMutationPermissions(
        ParsedQuery query,
        RuleSet rules,
        Dictionary<string, ResolvedTable> resolvedTables)
    {
        if (query.MutationTarget is null)
            return null;

        var targetKey = query.MutationTarget.Table;
        if (!resolvedTables.TryGetValue(targetKey, out var resolved))
            return null;

        if (!resolved.Rule.IsOperationAllowed(query.StatementType, rules.GlobalLimits))
        {
            return new ValidationResult(
                ValidationResultType.Invalid,
                $"{query.StatementType} is not allowed on table '{resolved.Schema}.{resolved.Table}'.");
        }

        return null;
    }

    /// <summary>
    /// Resolves each referenced table against the rule set.
    /// Returns a mapping of (schema, table) -> TableRule for resolved tables, or an error.
    /// Throws AmbiguousTableException when an unqualified table name matches multiple schemas.
    /// </summary>
    private static (Dictionary<string, ResolvedTable> ResolvedTables, ValidationResult? Error) ValidateTableAccess(
        ParsedQuery query,
        RuleSet rules)
    {
        var resolved = new Dictionary<string, ResolvedTable>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in query.ReferencedTables)
        {
            var resolution = ResolveTable(table, rules);

            if (resolution is null)
            {
                return (resolved, new ValidationResult(
                    ValidationResultType.Invalid,
                    $"Table '{table}' is not allowed or does not exist in the rule set."));
            }

            // Use the original table name (without schema) as the key for lookup
            resolved[table.Table] = resolution;
        }

        return (resolved, null);
    }

    /// <summary>
    /// Resolves a table reference against the rule set.
    /// - Schema-qualified: look up directly.
    /// - Unqualified: search all schemas. If found in multiple, throw AmbiguousTableException.
    /// </summary>
    private static ResolvedTable? ResolveTable(QualifiedTableName table, RuleSet rules)
    {
        if (table.Schema is not null)
        {
            // Schema-qualified lookup
            if (rules.ExposedSchemas.TryGetValue(table.Schema, out var schemaRule) &&
                schemaRule.Tables.TryGetValue(table.Table, out var tableRule))
            {
                return new ResolvedTable(table.Schema, table.Table, tableRule);
            }

            return null;
        }

        // Unqualified — search all schemas
        var matches = new List<ResolvedTable>();

        foreach (var (schemaName, schemaRule) in rules.ExposedSchemas)
        {
            if (schemaRule.Tables.TryGetValue(table.Table, out var tableRule))
            {
                matches.Add(new ResolvedTable(schemaName, table.Table, tableRule));
            }
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new AmbiguousTableException(
                $"Table '{table.Table}' is ambiguous — found in schemas: {string.Join(", ", matches.Select(m => m.Schema))}."),
        };
    }

    /// <summary>
    /// Builds a map from alias/table-reference name to actual table name by walking
    /// the FROM and JOIN clauses of the query AST.
    /// </summary>
    private static Dictionary<string, string> BuildAliasMap(ParsedQuery query)
    {
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (query.CteDefinitions is { } ctes)
        {
            foreach (var cte in ctes)
                aliasMap[cte.Name] = cte.Name;
        }

        if (query.Statement is not Statement.Select selectStmt)
            return aliasMap;

        if (selectStmt.Query.Body is not SetExpression.SelectExpression selectExpr)
            return aliasMap;

        var select = selectExpr.Select;
        if (select.From is null)
            return aliasMap;

        foreach (var tableWithJoins in select.From)
        {
            RegisterTableAlias(tableWithJoins.Relation, aliasMap);

            if (tableWithJoins.Joins is { } joins)
            {
                foreach (var join in joins)
                {
                    RegisterTableAlias(join.Relation, aliasMap);
                }
            }
        }

        return aliasMap;
    }

    private static void RegisterTableAlias(TableFactor? tableFactor, Dictionary<string, string> aliasMap)
    {
        if (tableFactor is not TableFactor.Table table)
            return;

        var names = table.Name.Values;
        if (names.Count == 0)
            return;

        var tableName = names[^1].Value;

        if (table.Alias is { } alias)
        {
            // alias -> actual table name
            aliasMap[alias.Name.Value] = tableName;
        }

        // Also register table name -> itself (for unaliased references like "products.id")
        aliasMap[tableName] = tableName;
    }

    private static ValidationResult? ValidateCteColumnAccess(
        IReadOnlyList<CteDefinition> cteDefinitions,
        Dictionary<string, ResolvedTable> resolvedTables)
    {
        foreach (var cte in cteDefinitions)
        {
            foreach (var column in cte.InnerReferencedColumns)
            {
                if (column.Table is not null)
                {
                    var tableRef = column.Table;

                    if (cte.InnerAliasMap.TryGetValue(tableRef, out var actualTableName))
                        tableRef = actualTableName;

                    if (!resolvedTables.TryGetValue(tableRef, out var resolved))
                        continue;

                    if (!resolved.Rule.IsColumnAllowed(column.Column))
                    {
                        return new ValidationResult(
                            ValidationResultType.Invalid,
                            $"Column '{column.Column}' is not allowed on table '{resolved.Table}' (referenced in CTE '{cte.Name}').");
                    }
                }
                else
                {
                    var allowedByAny = resolvedTables.Values
                                                     .Any(rt => rt.Rule.IsColumnAllowed(column.Column));

                    if (!allowedByAny)
                    {
                        return new ValidationResult(
                            ValidationResultType.Invalid,
                            $"Column '{column.Column}' is not allowed by any referenced table (referenced in CTE '{cte.Name}').");
                    }
                }
            }
        }

        return null;
    }

    private static ValidationResult? ValidateColumnAccess(
        ParsedQuery query,
        RuleSet rules,
        Dictionary<string, ResolvedTable> resolvedTables,
        Dictionary<string, string> aliasMap)
    {
        var cteNames = query.CteDefinitions is { } ctes
            ? new HashSet<string>(ctes.Select(c => c.Name), StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var column in query.ReferencedColumns)
        {
            if (column.Table is not null)
            {
                var tableRef = column.Table;

                if (aliasMap.TryGetValue(tableRef, out var actualTableName))
                {
                    tableRef = actualTableName;
                }

                if (cteNames is not null && cteNames.Contains(tableRef))
                    continue;

                if (!resolvedTables.TryGetValue(tableRef, out var resolved))
                {
                    return new ValidationResult(
                        ValidationResultType.Invalid,
                        $"Column '{column}' references table '{tableRef}' which is not in the rule set.");
                }

                if (!resolved.Rule.IsColumnAllowed(column.Column))
                {
                    return new ValidationResult(
                        ValidationResultType.Invalid,
                        $"Column '{column.Column}' is not allowed on table '{resolved.Table}'.");
                }
            }
            else
            {
                var allowedByAny = resolvedTables.Values
                                                 .Any(rt => rt.Rule.IsColumnAllowed(column.Column));

                if (!allowedByAny)
                {
                    return new ValidationResult(
                        ValidationResultType.Invalid,
                        $"Column '{column.Column}' is not allowed by any referenced table.");
                }
            }
        }

        return null;
    }

    private sealed record ResolvedTable(string Schema, string Table, TableRule Rule);
}