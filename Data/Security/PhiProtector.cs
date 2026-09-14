using System.Security.Cryptography;
using System.Text;

namespace MedicalManager.Data.Security;

public interface IPhiProtector
{
    string Protect(string? plaintext);
    string? Unprotect(string? stored);
    byte[]? ProtectBytes(byte[]? plaintext);
    byte[]? UnprotectBytes(byte[]? stored);
    void SelfTest();
}

/// <summary>
/// AES-256-GCM field encryption so PHI is ciphertext in the database.
/// Values are prefixed so existing plaintext rows stay readable until next save.
/// </summary>
public sealed class PhiProtector : IPhiProtector
{
    public const string StringPrefix = "enc:v1:";
    private static readonly byte[] BytesPrefix = "E1"u8.ToArray();

    private readonly byte[] _key;

    public PhiProtector(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["MEDICALMANAGER_PHI_KEY"]
            ?? Environment.GetEnvironmentVariable("MEDICALMANAGER_PHI_KEY");

        if (string.IsNullOrWhiteSpace(configured))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "MEDICALMANAGER_PHI_KEY is required in Production. Generate a 32-byte key (see DOCKER.md).");
            }

            configured = "MedicalManager-Development-Only-Not-For-Production";
        }

        _key = DecodeKey(configured);
    }

    public string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext ?? string.Empty;

        if (plaintext.StartsWith(StringPrefix, StringComparison.Ordinal))
            return plaintext;

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        return StringPrefix + Convert.ToBase64String(Seal(plainBytes));
    }

    public string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return stored;

        if (!stored.StartsWith(StringPrefix, StringComparison.Ordinal))
            return stored;

        var payload = Convert.FromBase64String(stored[StringPrefix.Length..]);
        return Encoding.UTF8.GetString(Open(payload));
    }

    public byte[]? ProtectBytes(byte[]? plaintext)
    {
        if (plaintext is null || plaintext.Length == 0)
            return plaintext;

        if (IsSealedBytes(plaintext))
            return plaintext;

        var sealedPayload = Seal(plaintext);
        var output = new byte[BytesPrefix.Length + sealedPayload.Length];
        BytesPrefix.CopyTo(output, 0);
        sealedPayload.CopyTo(output, BytesPrefix.Length);
        return output;
    }

    public byte[]? UnprotectBytes(byte[]? stored)
    {
        if (stored is null || stored.Length == 0)
            return stored;

        if (!IsSealedBytes(stored))
            return stored;

        var payload = stored.AsSpan(BytesPrefix.Length).ToArray();
        return Open(payload);
    }

    public void SelfTest()
    {
        const string sample = "phi-roundtrip-check";
        var encrypted = Protect(sample);
        var decrypted = Unprotect(encrypted);

        if (string.Equals(encrypted, sample, StringComparison.Ordinal))
            throw new CryptographicException("PHI encryption did not change the stored value.");

        if (!encrypted.StartsWith(StringPrefix, StringComparison.Ordinal))
            throw new CryptographicException("PHI encryption prefix is missing.");

        if (!string.Equals(decrypted, sample, StringComparison.Ordinal))
            throw new CryptographicException("PHI decryption failed the round-trip check.");

        var photo = Encoding.UTF8.GetBytes("photo-bytes");
        var sealedPhoto = ProtectBytes(photo);
        var openedPhoto = UnprotectBytes(sealedPhoto);
        if (sealedPhoto is null || openedPhoto is null || !photo.AsSpan().SequenceEqual(openedPhoto))
            throw new CryptographicException("PHI byte encryption failed the round-trip check.");
    }

    private byte[] Seal(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo(payload, 0);
        ciphertext.CopyTo(payload, nonce.Length);
        tag.CopyTo(payload, nonce.Length + ciphertext.Length);
        return payload;
    }

    private byte[] Open(byte[] payload)
    {
        if (payload.Length < 28)
            throw new CryptographicException("Encrypted payload is too short.");

        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(payload.Length - 16, 16);
        var ciphertext = payload.AsSpan(12, payload.Length - 28);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static bool IsSealedBytes(byte[] data) =>
        data.Length > BytesPrefix.Length
        && data[0] == BytesPrefix[0]
        && data[1] == BytesPrefix[1];

    private static byte[] DecodeKey(string configured)
    {
        try
        {
            var raw = Convert.FromBase64String(configured.Trim());
            if (raw.Length == 32)
                return raw;
        }
        catch (FormatException)
        {
            // Fall through to SHA-256 of the passphrase.
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }
}

public sealed class DisabledPhiProtector : IPhiProtector
{
    public static DisabledPhiProtector Instance { get; } = new();

    public string Protect(string? plaintext) => plaintext ?? string.Empty;
    public string? Unprotect(string? stored) => stored;
    public byte[]? ProtectBytes(byte[]? plaintext) => plaintext;
    public byte[]? UnprotectBytes(byte[]? stored) => stored;

    public void SelfTest()
    {
        throw new InvalidOperationException("PHI protector is disabled.");
    }
}
