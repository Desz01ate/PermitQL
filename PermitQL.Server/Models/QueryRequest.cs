namespace PermitQL.Server.Models;

public sealed record QueryRequest(string Query, string RuleSetKey);