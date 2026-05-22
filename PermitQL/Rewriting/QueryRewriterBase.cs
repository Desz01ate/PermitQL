namespace PermitQL.Rewriting;

using Abstractions;
using Models;
using SqlParser;
using SqlParser.Ast;

public abstract class QueryRewriterBase : IQueryRewriter
{
    private readonly IDataAccessor _dataAccessor;

    protected QueryRewriterBase(IDataAccessor dataAccessor)
    {
        _dataAccessor = dataAccessor;
    }

    protected record RowLimitResult(Expression? Limit, Top? Top, Fetch? Fetch);

    protected abstract RowLimitResult ApplyRowLimit(
        Expression? currentLimit,
        Top? currentTop,
        Fetch? currentFetch,
        RuleSet rules);

    public async ValueTask<string> RewriteAsync(
        ParsedQuery query,
        RuleSet rules,
        CancellationToken cancellationToken = default)
    {
        if (query.StatementType != StatementKind.Select)
        {
            return query.Statement.ToSql();
        }

        var originalSql = query.AsSelect().ToSql();
        var freshStatements = new SqlQueryParser().Parse(originalSql);
        var statement = (Statement.Select)freshStatements[0];
        var freshQuery = statement.Query;

        if (freshQuery.Body is not SetExpression.SelectExpression selectExpr)
        {
            return statement.ToSql();
        }

        var select = selectExpr.Select;

        var aliasMap = BuildAliasMap(select);
        var tableInfoMap = BuildTableInfoMap(select, aliasMap, rules);

        var modifiedProjection = await ExpandSelectStarAsync(
            select.Projection, tableInfoMap, rules, cancellationToken);

        Expression? modifiedSelection;
        Sequence<TableWithJoins>? modifiedFrom;

        if (select.From is { } fromClause)
        {
            (modifiedSelection, modifiedFrom) = InjectRowFilters(
                select.Selection, fromClause, tableInfoMap, aliasMap, rules);
        }
        else
        {
            modifiedSelection = select.Selection;
            modifiedFrom = select.From;
        }

        var rowLimit = ApplyRowLimit(freshQuery.Limit, select.Top, freshQuery.Fetch, rules);

        var modifiedSelect = select with
        {
            Projection = modifiedProjection,
            Selection = modifiedSelection,
            From = modifiedFrom,
            Top = rowLimit.Top,
        };
        var modifiedBody = selectExpr with { Select = modifiedSelect };
        var modifiedQuery = freshQuery with
        {
            Body = modifiedBody,
            Limit = rowLimit.Limit,
            Fetch = rowLimit.Fetch,
        };
        var modifiedStatement = statement with { Query = modifiedQuery };

        return modifiedStatement.ToSql();
    }

    private static Dictionary<string, string> BuildAliasMap(Select select)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (select.From is null)
            return map;

        foreach (var tableWithJoins in select.From)
        {
            RegisterAlias(tableWithJoins.Relation, map);

            if (tableWithJoins.Joins is { } joins)
            {
                foreach (var join in joins)
                {
                    RegisterAlias(join.Relation, map);
                }
            }
        }

        return map;
    }

    private static void RegisterAlias(TableFactor? tableFactor, Dictionary<string, string> map)
    {
        if (tableFactor is not TableFactor.Table table)
            return;

        var names = table.Name.Values;
        if (names.Count == 0)
            return;

        var tableName = names[^1].Value;

        if (table.Alias is { } alias)
        {
            map[alias.Name.Value] = tableName;
        }

        map[tableName] = tableName;
    }

    private static Dictionary<string, TableInfo> BuildTableInfoMap(
        Select select,
        Dictionary<string, string> aliasMap,
        RuleSet rules)
    {
        var map = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);

        if (select.From is null)
            return map;

        foreach (var tableWithJoins in select.From)
        {
            if (tableWithJoins.Relation is TableFactor.Table fromTable)
            {
                var tableName = fromTable.Name.Values[^1].Value;
                var alias = fromTable.Alias?.Name.Value ?? tableName;
                var rule = FindTableRule(tableName, rules);

                if (rule is not null)
                {
                    map[tableName] = new TableInfo(alias, rule, IsJoined: false);
                }
            }

            if (tableWithJoins.Joins is { } joins)
            {
                foreach (var join in joins)
                {
                    if (join.Relation is TableFactor.Table joinTable)
                    {
                        var tableName = joinTable.Name.Values[^1].Value;
                        var alias = joinTable.Alias?.Name.Value ?? tableName;
                        var rule = FindTableRule(tableName, rules);

                        if (rule is not null)
                        {
                            map[tableName] = new TableInfo(alias, rule, IsJoined: true);
                        }
                    }
                }
            }
        }

        return map;
    }

    private static TableRule? FindTableRule(string tableName, RuleSet rules)
    {
        foreach (var schema in rules.ExposedSchemas.Values)
        {
            if (schema.Tables.TryGetValue(tableName, out var rule))
                return rule;
        }

        return null;
    }

    private async ValueTask<Sequence<SelectItem>> ExpandSelectStarAsync(
        Sequence<SelectItem> projection,
        Dictionary<string, TableInfo> tableInfoMap,
        RuleSet rules,
        CancellationToken ct)
    {
        var hasWildcard = false;

        foreach (var item in projection)
        {
            if (item is SelectItem.Wildcard)
            {
                hasWildcard = true;
                break;
            }
        }

        if (!hasWildcard)
            return projection;

        var newProjection = new Sequence<SelectItem>();

        foreach (var item in projection)
        {
            if (item is not SelectItem.Wildcard)
            {
                newProjection.Add(item);
                continue;
            }

            foreach (var (tableName, info) in tableInfoMap)
            {
                var columns = await GetAllowedColumnsAsync(tableName, info.Rule, ct);

                foreach (var column in columns)
                {
                    newProjection.Add(new SelectItem.UnnamedExpression(
                        new Expression.Identifier(new Ident(column))));
                }
            }
        }

        return newProjection;
    }

    private async ValueTask<IReadOnlyList<string>> GetAllowedColumnsAsync(
        string tableName,
        TableRule rule,
        CancellationToken ct)
    {
        if (!rule.IsWildcard)
            return rule.AllowedColumns;

        var metadataQuery = $"SELECT * FROM {tableName} WHERE 1=0";
        var columnDefs = await _dataAccessor.GetColumnDefinitionAsync(metadataQuery, ct);

        var denied = rule.DeniedColumns ?? [];
        var deniedSet = new HashSet<string>(denied, StringComparer.OrdinalIgnoreCase);

        return columnDefs
               .Where(c => !deniedSet.Contains(c.Name))
               .Select(c => c.Name)
               .ToList();
    }

    private static (Expression? ModifiedSelection, Sequence<TableWithJoins> ModifiedFrom) InjectRowFilters(
        Expression? currentSelection,
        Sequence<TableWithJoins> from,
        Dictionary<string, TableInfo> tableInfoMap,
        Dictionary<string, string> aliasMap,
        RuleSet rules)
    {
        var modifiedSelection = currentSelection;
        var modifiedFrom = new Sequence<TableWithJoins>();

        foreach (var tableWithJoins in from)
        {
            if (tableWithJoins.Relation is TableFactor.Table fromTable)
            {
                var tableName = fromTable.Name.Values[^1].Value;

                if (tableInfoMap.TryGetValue(tableName, out var info) && !info.IsJoined)
                {
                    if (info.Rule.RowFilter is { } filter)
                    {
                        var filterExpr = ParseFilterExpression(filter);
                        var qualifiedFilter = QualifyExpression(filterExpr, info.Alias);

                        modifiedSelection = modifiedSelection is not null
                            ? new Expression.BinaryOp(
                                new Expression.Nested(modifiedSelection),
                                BinaryOperator.And,
                                new Expression.Nested(qualifiedFilter))
                            : qualifiedFilter;
                    }
                }
            }

            if (tableWithJoins.Joins is { Count: > 0 } joins)
            {
                var modifiedJoins = new Sequence<Join>();

                foreach (var join in joins)
                {
                    var modifiedJoin = InjectRowFilterIntoJoin(join, tableInfoMap);
                    modifiedJoins.Add(modifiedJoin);
                }

                modifiedFrom.Add(tableWithJoins with { Joins = modifiedJoins });
            }
            else
            {
                modifiedFrom.Add(tableWithJoins);
            }
        }

        return (modifiedSelection, modifiedFrom);
    }

    private static Join InjectRowFilterIntoJoin(
        Join join,
        Dictionary<string, TableInfo> tableInfoMap)
    {
        if (join.Relation is not TableFactor.Table joinTable)
            return join;

        var tableName = joinTable.Name.Values[^1].Value;

        if (!tableInfoMap.TryGetValue(tableName, out var info) || info.Rule.RowFilter is null)
            return join;

        var filterExpr = ParseFilterExpression(info.Rule.RowFilter);
        var qualifiedFilter = QualifyExpression(filterExpr, info.Alias);

        var modifiedJoinOp = join.JoinOperator switch
        {
            JoinOperator.Inner inner when inner.JoinConstraint is JoinConstraint.On on =>
                (JoinOperator)(inner with
                {
                    JoinConstraint = on with
                    {
                        Expression = new Expression.BinaryOp(
                            new Expression.Nested(on.Expression),
                            BinaryOperator.And,
                            new Expression.Nested(qualifiedFilter)),
                    },
                }),

            JoinOperator.LeftOuter left when left.JoinConstraint is JoinConstraint.On on =>
                left with
                {
                    JoinConstraint = on with
                    {
                        Expression = new Expression.BinaryOp(
                            new Expression.Nested(on.Expression),
                            BinaryOperator.And,
                            new Expression.Nested(qualifiedFilter)),
                    },
                },

            JoinOperator.RightOuter right when right.JoinConstraint is JoinConstraint.On on =>
                right with
                {
                    JoinConstraint = on with
                    {
                        Expression = new Expression.BinaryOp(
                            new Expression.Nested(on.Expression),
                            BinaryOperator.And,
                            new Expression.Nested(qualifiedFilter)),
                    },
                },

            JoinOperator.FullOuter full when full.JoinConstraint is JoinConstraint.On on =>
                full with
                {
                    JoinConstraint = on with
                    {
                        Expression = new Expression.BinaryOp(
                            new Expression.Nested(on.Expression),
                            BinaryOperator.And,
                            new Expression.Nested(qualifiedFilter)),
                    },
                },

            _ => join.JoinOperator,
        };

        return join with { JoinOperator = modifiedJoinOp };
    }

    private static Expression ParseFilterExpression(string filter)
    {
        var wrappedSql = $"SELECT 1 WHERE {filter}";
        var statements = new SqlQueryParser().Parse(wrappedSql);
        var stmt = (Statement.Select)statements[0];
        var selectExpr = (SetExpression.SelectExpression)stmt.Query.Body;
        return selectExpr.Select.Selection
               ?? throw new InvalidOperationException($"Failed to parse row filter: {filter}");
    }

    private static Expression QualifyExpression(Expression expr, string alias)
    {
        return expr switch
        {
            Expression.Identifier id =>
                new Expression.CompoundIdentifier(
                    new Sequence<Ident> { new Ident(alias), id.Ident }),

            Expression.CompoundIdentifier => expr,

            Expression.BinaryOp bin =>
                new Expression.BinaryOp(
                    QualifyExpression(bin.Left, alias),
                    bin.Op,
                    QualifyExpression(bin.Right, alias)),

            Expression.UnaryOp unary =>
                new Expression.UnaryOp(
                    QualifyExpression(unary.Expression, alias),
                    unary.Op),

            Expression.Nested nested =>
                new Expression.Nested(QualifyExpression(nested.Expression, alias)),

            Expression.IsNull isNull =>
                new Expression.IsNull(QualifyExpression(isNull.Expression, alias)),

            Expression.IsNotNull isNotNull =>
                new Expression.IsNotNull(QualifyExpression(isNotNull.Expression, alias)),

            Expression.Between between =>
                new Expression.Between(
                    QualifyExpression(between.Expression, alias),
                    between.Negated,
                    QualifyExpression(between.Low, alias),
                    QualifyExpression(between.High, alias)),

            _ => expr,
        };
    }

    protected sealed record TableInfo(string Alias, TableRule Rule, bool IsJoined);
}