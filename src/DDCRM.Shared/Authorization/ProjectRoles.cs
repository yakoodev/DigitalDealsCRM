namespace DDCRM.Shared.Authorization;

public static class ProjectRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Moderator = "moderator";

    public static readonly HashSet<string> All =
    [
        Owner,
        Admin,
        Moderator,
    ];
}
