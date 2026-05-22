namespace PermitQL.Models;

public enum ValidationResultType
{
    Valid,
    Invalid,
}

public record ValidationResult(ValidationResultType Type, string? Message);