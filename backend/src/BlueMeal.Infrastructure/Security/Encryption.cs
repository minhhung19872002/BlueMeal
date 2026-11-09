using System.Security.Cryptography;
using System.Text;
using BlueMeal.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BlueMeal.Infrastructure.Security;

/// <summary>
/// AES-256-GCM column encryption with key rotation (ASSUMPTIONS A-28).
/// Ciphertext format: <c>v1:{kid}:{base64(nonce[12] | cipher | tag[16])}</c>.
/// Keys: <c>BLUEMEAL_ENC_KEYS="kid1:base64key;kid0:base64key"</c> — first key encrypts, all keys decrypt.
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const string Prefix = "v1:";
    private readonly (string Kid, byte[] Key) _current;
    private readonly Dictionary<string, byte[]> _keys;
    private readonly byte[] _hmacKey;

    public AesGcmEncryptionService(string keySpec, string hmacKeyBase64)
    {
        _keys = new Dictionary<string, byte[]>();
        foreach (var part in keySpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf(':');
            if (idx <= 0) throw new InvalidOperationException("Invalid encryption key spec. Expected kid:base64key");
            var kid = part[..idx];
            var key = Convert.FromBase64String(part[(idx + 1)..]);
            if (key.Length != 32) throw new InvalidOperationException($"Encryption key '{kid}' must be 256-bit.");
            _keys[kid] = key;
            if (_current.Kid is null) _current = (kid, key);
        }
        if (_keys.Count == 0) throw new InvalidOperationException("No encryption key configured (BLUEMEAL_ENC_KEYS).");
        _hmacKey = Convert.FromBase64String(hmacKeyBase64);
        if (_hmacKey.Length < 32) throw new InvalidOperationException("HMAC key must be at least 256-bit.");
    }

    public string CurrentKeyId => _current.Kid;

    public string? Encrypt(string? plain)
    {
        if (plain is null) return null;
        var data = Encoding.UTF8.GetBytes(plain);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_current.Key, 16))
            aes.Encrypt(nonce, data, cipher, tag, Encoding.ASCII.GetBytes(_current.Kid));
        var blob = new byte[12 + cipher.Length + 16];
        nonce.CopyTo(blob, 0); cipher.CopyTo(blob, 12); tag.CopyTo(blob, 12 + cipher.Length);
        return $"{Prefix}{_current.Kid}:{Convert.ToBase64String(blob)}";
    }

    public string? Decrypt(string? stored)
    {
        if (stored is null) return null;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // legacy plaintext (pre-encryption rows)
        var rest = stored[Prefix.Length..];
        var idx = rest.IndexOf(':');
        var kid = rest[..idx];
        if (!_keys.TryGetValue(kid, out var key)) throw new CryptographicException($"Unknown encryption key id '{kid}'.");
        var blob = Convert.FromBase64String(rest[(idx + 1)..]);
        var nonce = blob.AsSpan(0, 12);
        var cipher = blob.AsSpan(12, blob.Length - 28);
        var tag = blob.AsSpan(blob.Length - 16, 16);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain, Encoding.ASCII.GetBytes(kid));
        return Encoding.UTF8.GetString(plain);
    }

    public string? Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        return Convert.ToHexString(HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>Returns true when the ciphertext was produced with an old key (needs re-encryption).</summary>
    public bool NeedsRotation(string? stored) =>
        stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal) && !stored.StartsWith($"{Prefix}{_current.Kid}:", StringComparison.Ordinal);

    public static string GenerateKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}

/// <summary>
/// EF Core value converter for [Encrypted] properties. EF caches the model, so the converter resolves the service
/// through a static accessor configured once at startup.
/// </summary>
public sealed class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public static IEncryptionService? Service { get; set; }
    public static readonly EncryptedStringConverter Instance = new();

    private EncryptedStringConverter() : base(
        v => Encrypt(v),
        v => Decrypt(v))
    { }

    private static string? Encrypt(string? v) => v is null ? null : (Service ?? throw new InvalidOperationException("Encryption service not initialized")).Encrypt(v);
    private static string? Decrypt(string? v) => v is null ? null : (Service ?? throw new InvalidOperationException("Encryption service not initialized")).Decrypt(v);
}
