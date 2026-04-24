using System.ComponentModel.DataAnnotations;

namespace DDCRM.Shared.Idempotency;

public sealed class IdempotencyRecord
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(160)]
    public required string Scope { get; set; }

    [MaxLength(200)]
    public required string Key { get; set; }

    public required int StatusCode { get; set; }

    public required string PayloadJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
