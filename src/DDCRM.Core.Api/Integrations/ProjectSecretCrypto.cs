using System.Security.Cryptography;
using System.Text;

namespace DDCRM.Core.Api.Integrations;

public sealed class ProjectSecretCrypto(IConfiguration configuration)
{
    private readonly byte[] _key = BuildKey(configuration["CORE_SECRETS_ENCRYPTION_KEY"]);

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var memory = new MemoryStream();
        memory.Write(aes.IV, 0, aes.IV.Length);
        using (var crypto = new CryptoStream(memory, encryptor, CryptoStreamMode.Write, leaveOpen: true))
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            crypto.Write(plaintextBytes, 0, plaintextBytes.Length);
            crypto.FlushFinalBlock();
        }

        return Convert.ToBase64String(memory.ToArray());
    }

    public string Decrypt(string ciphertext)
    {
        var payload = Convert.FromBase64String(ciphertext);
        if (payload.Length < 17)
        {
            throw new InvalidOperationException("Некорректный ciphertext.");
        }

        var iv = payload[..16];
        var body = payload[16..];

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var memory = new MemoryStream(body);
        using var crypto = new CryptoStream(memory, decryptor, CryptoStreamMode.Read);
        using var reader = new StreamReader(crypto, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static byte[] BuildKey(string? raw)
    {
        var source = string.IsNullOrWhiteSpace(raw)
            ? "ddcrm-local-default-encryption-key-change-me"
            : raw.Trim();

        return SHA256.HashData(Encoding.UTF8.GetBytes(source));
    }
}
