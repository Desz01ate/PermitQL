namespace PermitQL.Rewriting.Dialects;

using Abstractions;
using Models;
using SqlParser.Ast;

public sealed class LimitQueryRewriter : QueryRewriterBase
{
    public LimitQueryRewriter(IDataAccessor dataAccessor) : base(dataAccessor)
    {
    }

    protected override RowLimitResult ApplyRowLimit(
        Expression? currentLimit,
        Top? currentTop,
        Fetch? currentFetch,
        RuleSet rules)
    {
        var maxRows = rules.GlobalLimits.MaxRowsReturned;

        if (currentLimit is Expression.LiteralValue lv && lv.Value is Value.Number num)
        {
            if (int.TryParse(num.Value, out var currentValue) && currentValue <= maxRows)
                return new RowLimitResult(currentLimit, Top: null, Fetch: null);
        }

        var limit = new Expression.LiteralValue(new Value.Number(maxRows.ToString(), false));
        return new RowLimitResult(limit, Top: null, Fetch: null);
    }
}