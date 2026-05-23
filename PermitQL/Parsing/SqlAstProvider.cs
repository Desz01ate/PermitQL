namespace PermitQL.Parsing;

using System.Collections.Concurrent;
using Abstractions;
using Exceptions;
using Models;
using SqlParser;
using SqlParser.Ast;

public sealed class SqlAstProvider : ISqlAstProvider
{
    private readonly ConcurrentDictionary<string, ParsedQuery> _cache = new();
    private readonly int _maxCacheSize;

    public SqlAstProvider(int maxCacheSize = 10_000)
    {
        this._maxCacheSize = maxCacheSize;
    }

    public ParsedQuery GetOrParse(string query)
    {
        if (this._cache.TryGetValue(query, out var cached))
            return cached;

        var parsed = Parse(query);

        if (this._cache.Count >= this._maxCacheSize) this._cache.Clear();

        this._cache.TryAdd(query, parsed);
        return parsed;
    }

    private static ParsedQuery Parse(string sql)
    {
        Sequence<Statement> statements;

        try
        {
            statements = new SqlQueryParser().Parse(sql);
        }
        catch (Exception ex)
        {
            throw new SqlParseException($"Failed to parse SQL: {ex.Message}", ex);
        }

        if (statements.Count == 0)
            throw new SqlParseException("No SQL statements found in input.");

        var statement = statements[0];
        var statementKind = ClassifyStatement(statement);

        var tables = new HashSet<QualifiedTableName>();
        var columns = new HashSet<QualifiedColumnName>();
        QualifiedTableName? mutationTarget = null;

        List<CteDefinition>? cteDefinitions = null;

        switch (statement)
        {
            case Statement.Select selectStmt:
                cteDefinitions = ExtractCteDefinitions(selectStmt.Query.With, tables);
                ExtractFromQuery(selectStmt.Query, tables, columns);
                RemoveCteNamesFromTables(cteDefinitions, tables);
                break;

            case Statement.Insert insertStmt:
                mutationTarget = ExtractFromObjectName(insertStmt.InsertOperation.Name);
                if (mutationTarget is not null)
                    tables.Add(mutationTarget);
                break;

            case Statement.Update updateStmt:
                mutationTarget = ExtractFromTableFactor(updateStmt.Table.Relation);
                if (mutationTarget is not null)
                    tables.Add(mutationTarget);
                break;

            case Statement.Delete deleteStmt:
                mutationTarget = ExtractDeleteTarget(deleteStmt.DeleteOperation);
                if (mutationTarget is not null)
                    tables.Add(mutationTarget);
                break;
        }

        return new ParsedQuery(statement, tables, columns, statementKind, mutationTarget, cteDefinitions);
    }

    private static StatementKind ClassifyStatement(Statement statement) =>
        statement switch
        {
            Statement.Select => StatementKind.Select,
            Statement.Insert => StatementKind.Insert,
            Statement.Update => StatementKind.Update,
            Statement.Delete => StatementKind.Delete,
            _ => StatementKind.Other,
        };

    private static void ExtractFromQuery(
        Query query,
        HashSet<QualifiedTableName> tables,
        HashSet<QualifiedColumnName> columns)
    {
        ExtractFromSetExpression(query.Body, tables, columns);

        if (query.OrderBy is { Expressions: { } orderByExprs })
        {
            foreach (var orderExpr in orderByExprs)
            {
                ExtractColumnsFromExpression(orderExpr.Expression, columns);
            }
        }
    }

    private static void ExtractFromSetExpression(
        SetExpression? body,
        HashSet<QualifiedTableName> tables,
        HashSet<QualifiedColumnName> columns)
    {
        switch (body)
        {
            case SetExpression.SelectExpression selectExpr:
                ExtractFromSelect(selectExpr.Select, tables, columns);
                break;

            case SetExpression.SetOperation setOp:
                ExtractFromSetExpression(setOp.Left, tables, columns);
                ExtractFromSetExpression(setOp.Right, tables, columns);
                break;
        }
    }

    private static void ExtractFromSelect(
        Select select,
        HashSet<QualifiedTableName> tables,
        HashSet<QualifiedColumnName> columns)
    {
        // Extract from projection
        foreach (var item in select.Projection)
        {
            switch (item)
            {
                case SelectItem.UnnamedExpression unnamed:
                    ExtractColumnsFromExpression(unnamed.Expression, columns);
                    break;
                case SelectItem.ExpressionWithAlias withAlias:
                    ExtractColumnsFromExpression(withAlias.Expression, columns);
                    break;
                // SelectItem.Wildcard — no explicit columns to extract
            }
        }

        // Extract from FROM clause
        if (select.From is { } fromClauses)
        {
            foreach (var tableWithJoins in fromClauses)
            {
                if (tableWithJoins.Relation is { } relation)
                    ExtractFromTableFactor(relation, tables);

                if (tableWithJoins.Joins is { } joins)
                {
                    foreach (var join in joins)
                    {
                        if (join.Relation is { } joinRelation)
                            ExtractFromTableFactor(joinRelation, tables);

                        if (join.JoinOperator is { } joinOp)
                        {
                            var constraint = GetJoinConstraint(joinOp);

                            if (constraint is JoinConstraint.On onConstraint)
                            {
                                ExtractColumnsFromExpression(onConstraint.Expression, columns);
                            }
                        }
                    }
                }
            }
        }

        // Extract from WHERE clause
        if (select.Selection is { } whereExpr)
        {
            ExtractColumnsFromExpression(whereExpr, columns);
        }

        // Extract from GROUP BY clause
        if (select.GroupBy is GroupByExpression.Expressions groupBy)
        {
            foreach (var expr in groupBy.ColumnNames)
            {
                ExtractColumnsFromExpression(expr, columns);
            }
        }

        // Extract from HAVING clause
        if (select.Having is { } havingExpr)
        {
            ExtractColumnsFromExpression(havingExpr, columns);
        }
    }

    private static JoinConstraint? GetJoinConstraint(JoinOperator joinOp) =>
        joinOp switch
        {
            JoinOperator.Inner inner => inner.JoinConstraint,
            JoinOperator.LeftOuter left => left.JoinConstraint,
            JoinOperator.RightOuter right => right.JoinConstraint,
            JoinOperator.FullOuter full => full.JoinConstraint,
            JoinOperator.CrossJoin => null,
            _ => null,
        };

    private static void ExtractFromTableFactor(
        TableFactor tableFactor,
        HashSet<QualifiedTableName> tables)
    {
        var name = ExtractFromTableFactor(tableFactor);
        if (name is not null)
            tables.Add(name);
    }

    private static QualifiedTableName? ExtractFromTableFactor(TableFactor? tableFactor)
    {
        if (tableFactor is not TableFactor.Table table)
            return null;

        return ExtractFromObjectName(table.Name);
    }

    private static QualifiedTableName? ExtractFromObjectName(ObjectName objectName)
    {
        var names = objectName.Values;
        return names.Count switch
        {
            >= 2 => new QualifiedTableName(
                Schema: names[names.Count - 2].Value,
                Table: names[names.Count - 1].Value),
            1 => new QualifiedTableName(Schema: null, Table: names[0].Value),
            _ => null,
        };
    }

    private static QualifiedTableName? ExtractDeleteTarget(DeleteOperation deleteOp)
    {
        if (deleteOp.Tables is { Count: > 0 } targets)
            return ExtractFromObjectName(targets[0]);

        if (deleteOp.From?.From is { Count: > 0 } fromTables)
            return ExtractFromTableFactor(fromTables[0].Relation);

        return null;
    }

    private static void ExtractColumnsFromExpression(
        Expression expression,
        HashSet<QualifiedColumnName> columns)
    {
        switch (expression)
        {
            case Expression.Identifier ident:
                columns.Add(new QualifiedColumnName(Schema: null, Table: null, Column: ident.Ident.Value));
                break;

            case Expression.CompoundIdentifier compound:
                var idents = compound.Idents;
                var col = idents.Count switch
                {
                    >= 3 => new QualifiedColumnName(
                        Schema: idents[idents.Count - 3].Value,
                        Table: idents[idents.Count - 2].Value,
                        Column: idents[idents.Count - 1].Value),
                    2 => new QualifiedColumnName(
                        Schema: null,
                        Table: idents[0].Value,
                        Column: idents[1].Value),
                    1 => new QualifiedColumnName(
                        Schema: null,
                        Table: null,
                        Column: idents[0].Value),
                    _ => null,
                };
                if (col is not null)
                    columns.Add(col);
                break;

            case Expression.BinaryOp binaryOp:
                ExtractColumnsFromExpression(binaryOp.Left, columns);
                ExtractColumnsFromExpression(binaryOp.Right, columns);
                break;

            case Expression.UnaryOp unaryOp:
                ExtractColumnsFromExpression(unaryOp.Expression, columns);
                break;

            case Expression.Nested nested:
                ExtractColumnsFromExpression(nested.Expression, columns);
                break;

            case Expression.IsNull isNull:
                ExtractColumnsFromExpression(isNull.Expression, columns);
                break;

            case Expression.IsNotNull isNotNull:
                ExtractColumnsFromExpression(isNotNull.Expression, columns);
                break;

            case Expression.Between between:
                ExtractColumnsFromExpression(between.Expression, columns);
                ExtractColumnsFromExpression(between.Low, columns);
                ExtractColumnsFromExpression(between.High, columns);
                break;

            case Expression.InList inList:
                ExtractColumnsFromExpression(inList.Expression, columns);
                foreach (var item in inList.List)
                    ExtractColumnsFromExpression(item, columns);
                break;

            case Expression.Function func:
                ExtractColumnsFromFunctionArgs(func.Args, columns);
                break;
        }
    }

    private static void ExtractColumnsFromFunctionArgs(
        FunctionArguments funcArgs,
        HashSet<QualifiedColumnName> columns)
    {
        if (funcArgs is not FunctionArguments.List list)
            return;

        if (list.ArgumentList is not { Args: { } args })
            return;

        foreach (var arg in args)
        {
            if (arg is FunctionArg.Unnamed unnamed
                && unnamed.FunctionArgExpression is FunctionArgExpression.FunctionExpression funcExpr)
            {
                ExtractColumnsFromExpression(funcExpr.Expression, columns);
            }
        }
    }

    private static List<CteDefinition>? ExtractCteDefinitions(
        With? withClause,
        HashSet<QualifiedTableName> outerTables)
    {
        if (withClause is null)
            return null;

        var cteDefinitions = new List<CteDefinition>();
        var cteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cte in withClause.CteTables)
        {
            var cteName = cte.Alias.Name.Value;
            cteNames.Add(cteName);

            var columnAliases = cte.Alias.Columns is { Count: > 0 } cols
                ? cols.Select(c => c.Value).ToList()
                : null;

            var cteTables = new HashSet<QualifiedTableName>();
            var cteColumns = new HashSet<QualifiedColumnName>();
            ExtractFromQuery(cte.Query, cteTables, cteColumns);

            cteTables.RemoveWhere(t => t.Schema is null && cteNames.Contains(t.Table));

            var aliasMap = BuildCteAliasMap(cte.Query);

            outerTables.UnionWith(cteTables);

            cteDefinitions.Add(new CteDefinition(
                cteName,
                columnAliases,
                cteTables,
                cteColumns,
                aliasMap));
        }

        return cteDefinitions;
    }

    private static void RemoveCteNamesFromTables(
        List<CteDefinition>? cteDefinitions,
        HashSet<QualifiedTableName> tables)
    {
        if (cteDefinitions is null)
            return;

        foreach (var cte in cteDefinitions)
        {
            tables.RemoveWhere(t => t.Schema is null
                && string.Equals(t.Table, cte.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static Dictionary<string, string> BuildCteAliasMap(Query query)
    {
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectAliasesFromSetExpression(query.Body, aliasMap);
        return aliasMap;
    }

    private static void CollectAliasesFromSetExpression(
        SetExpression? body,
        Dictionary<string, string> aliasMap)
    {
        switch (body)
        {
            case SetExpression.SelectExpression selectExpr:
                CollectAliasesFromSelect(selectExpr.Select, aliasMap);
                break;

            case SetExpression.SetOperation setOp:
                CollectAliasesFromSetExpression(setOp.Left, aliasMap);
                CollectAliasesFromSetExpression(setOp.Right, aliasMap);
                break;
        }
    }

    private static void CollectAliasesFromSelect(Select select, Dictionary<string, string> aliasMap)
    {
        if (select.From is null)
            return;

        foreach (var tableWithJoins in select.From)
        {
            RegisterAlias(tableWithJoins.Relation, aliasMap);

            if (tableWithJoins.Joins is { } joins)
            {
                foreach (var join in joins)
                    RegisterAlias(join.Relation, aliasMap);
            }
        }
    }

    private static void RegisterAlias(TableFactor? tableFactor, Dictionary<string, string> aliasMap)
    {
        if (tableFactor is not TableFactor.Table table)
            return;

        var names = table.Name.Values;
        if (names.Count == 0)
            return;

        var tableName = names[^1].Value;

        if (table.Alias is { } alias)
            aliasMap[alias.Name.Value] = tableName;

        aliasMap[tableName] = tableName;
    }
}