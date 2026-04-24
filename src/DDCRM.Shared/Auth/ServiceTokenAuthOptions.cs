namespace DDCRM.Shared.Auth;

public sealed class ServiceTokenAuthOptions
{
    public bool Enabled { get; set; } = true;

    public string[] AcceptedTokens { get; set; } = [];

    public string[] ForbiddenTokens { get; set; } = [];
}
