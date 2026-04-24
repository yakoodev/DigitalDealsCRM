namespace DDCRM.Core.Api.Billing;

public sealed class BillingClientOptions
{
    public const string SectionName = "BillingClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:5122";

    public string? ServiceToken { get; set; }
}
