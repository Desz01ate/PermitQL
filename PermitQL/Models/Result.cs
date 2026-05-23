namespace PermitQL.Models;

public sealed class Result<TSucc, TErr>
{
    public TSucc? Success { get; private init; }

    public TErr? Error { get; private init; }

    public void Match(Action<TSucc> success, Action<TErr> error)
    {
        if (this.Success is not null)
        {
            success(this.Success);
        }
        else if (this.Error is not null)
        {
            error(this.Error);
        }
    }

    public T Match<T>(Func<TSucc, T> success, Func<TErr, T> error)
    {
        if (this.Success is not null)
        {
            return success(this.Success);
        }

        if (this.Error is not null)
        {
            return error(this.Error);
        }

        throw new InvalidOperationException("Result is neither success nor error");
    }

    public static Result<TSucc, TErr> Succ(TSucc value)
    {
        return new Result<TSucc, TErr> { Success = value };
    }

    public static Result<TSucc, TErr> Err(TErr error)
    {
        return new Result<TSucc, TErr> { Error = error };
    }

    public static implicit operator Result<TSucc, TErr>(TSucc value)
    {
        return Succ(value);
    }

    public static implicit operator Result<TSucc, TErr>(TErr error)
    {
        return Err(error);
    }
}