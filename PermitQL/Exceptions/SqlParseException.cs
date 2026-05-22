namespace PermitQL.Exceptions;

public class SqlParseException(string message, Exception? innerException = null)
    : Exception(message, innerException);