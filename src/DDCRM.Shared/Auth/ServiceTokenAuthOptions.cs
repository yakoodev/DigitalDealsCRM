namespace DDCRM.Shared.Auth;

public sealed class ServiceTokenAuthOptions
{
    public bool Enabled { get; set; } = true;

    public string[] AcceptedTokens { get; set; } = [];

    public string[] ForbiddenTokens { get; set; } = [];

    public string MissingTokenErrorCode { get; set; } = Errors.ApiErrorCodes.Unauthorized;

    public string MissingTokenErrorMessage { get; set; } = "Заголовок X-Service-Token обязателен.";

    public string InvalidTokenErrorCode { get; set; } = Errors.ApiErrorCodes.Unauthorized;

    public string InvalidTokenErrorMessage { get; set; } = "Невалидный service-auth токен.";

    public string ForbiddenTokenErrorCode { get; set; } = Errors.ApiErrorCodes.Forbidden;

    public string ForbiddenTokenErrorMessage { get; set; } = "Токен не может использоваться в internal-контуре.";
}
