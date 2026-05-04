using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class TelegramProxyProfileEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(120)]
    public required string Name { get; set; }

    [MaxLength(32)]
    public required string ProxyType { get; set; }

    [MaxLength(255)]
    public required string Host { get; set; }

    public int Port { get; set; }

    public string? LoginCiphertext { get; set; }

    public string? PasswordCiphertext { get; set; }

    public bool IsActive { get; set; }

    public Guid UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
