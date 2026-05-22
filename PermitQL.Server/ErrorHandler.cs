namespace PermitQL.Server;

using PermitQL.Exceptions;

public static class ErrorHandler
{
    public static (string Message, string Type, int StatusCode) Classify(Exception ex) => ex switch
    {
        QueryValidationFailedException e => (e.Message, "validation_error", 400),
        SqlParseException e => (e.Message, "parse_error", 400),
        AmbiguousTableException e => (e.Message, "ambiguous_table", 400),
        KeyNotFoundException e => (e.Message, "not_found", 404),
        OperationCanceledException => ("Query execution timed out.", "timeout", 408),
        _ => ("An unexpected error occurred.", "internal_error", 500),
    };
}