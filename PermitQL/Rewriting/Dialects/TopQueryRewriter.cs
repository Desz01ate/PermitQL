namespace PermitQL.Rewriting.Dialects;

using Abstractions;
using Models;
using SqlParser.Ast;

public sealed class TopQueryRewriter : QueryRewriterBase
{
    public TopQueryRewriter(IDataAccessor dataAccessor) : base(dataAccessor)
    {
    }

    protected override RowLimitResult ApplyRowLimit(
        Expression? currentLimit,
        Top? currentTop,
        Fetch? currentFetch,
        RuleSet rules)
    {
        var maxRows = rules.GlobalLimits.MaxRowsReturned;

        if (currentTop?.Quantity is TopQuantity.Constant constant
            && constant.Quantity <= maxRows)
        {
            return new RowLimitResult(Limit: null, Top: currentTop, Fetch: null);
        }

        var top = new Top(new TopQuantity.Constant(maxRows), false, false);
        return new RowLimitResult(Limit: null, Top: top, Fetch: null);
    }
}