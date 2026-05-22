namespace PermitQL.Abstractions;

using Models;

public interface ISqlAstProvider
{
    ParsedQuery GetOrParse(string query);
}