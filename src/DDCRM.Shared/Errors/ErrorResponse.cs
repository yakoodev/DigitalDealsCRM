namespace DDCRM.Shared.Errors;

public sealed record ErrorResponse(string ErrorCode, string Message, string RequestId, object? Details = null);
