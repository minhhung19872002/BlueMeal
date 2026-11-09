using BlueMeal.Domain.Common;
using BlueMeal.Infrastructure;
using BlueMeal.Infrastructure.Security;

namespace BlueMeal.UnitTests.Common;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Thịt Lợn Đùi", "thit lon dui")]
    [InlineData("  Rau   muống  ", "rau muong")]
    [InlineData("Cà chua", "ca chua")]
    [InlineData("ĐẬU PHỤ", "dau phu")]
    [InlineData("", "")]
    public void Normalizes_vietnamese_for_accent_insensitive_search(string input, string expected)
        => TextNormalizer.Normalize(input).Should().Be(expected);
}

public class EncryptionTests
{
    private static readonly string K1 = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string K2 = Convert.ToBase64String(Enumerable.Range(50, 32).Select(i => (byte)i).ToArray());
    private static readonly string Hmac = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void Round_trips_and_is_randomized()
    {
        var svc = new AesGcmEncryptionService($"k1:{K1}", Hmac);
        var a = svc.Encrypt("0912345678");
        var b = svc.Encrypt("0912345678");
        a.Should().StartWith("v1:k1:").And.NotBe(b);
        svc.Decrypt(a).Should().Be("0912345678");
    }

    [Fact]
    public void Supports_key_rotation()
    {
        var old = new AesGcmEncryptionService($"k1:{K1}", Hmac);
        var cipher = old.Encrypt("secret");
        var rotated = new AesGcmEncryptionService($"k2:{K2};k1:{K1}", Hmac);
        rotated.Decrypt(cipher).Should().Be("secret");
        rotated.NeedsRotation(cipher).Should().BeTrue();
        rotated.Encrypt("x")!.Should().StartWith("v1:k2:");
    }

    [Fact]
    public void Tampering_is_detected()
    {
        var svc = new AesGcmEncryptionService($"k1:{K1}", Hmac);
        var c = svc.Encrypt("hello")!;
        var tampered = c[..^4] + (c[^4] == 'A' ? "B" : "A") + c[^3..];
        var act = () => svc.Decrypt(tampered);
        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Hash_is_deterministic_and_case_insensitive()
    {
        var svc = new AesGcmEncryptionService($"k1:{K1}", Hmac);
        svc.Hash("A@b.vn").Should().Be(svc.Hash(" a@B.VN "));
        svc.Hash(null).Should().BeNull();
    }
}

public class CronTests
{
    [Theory]
    [InlineData("0 2 * * *", "0 0 2 * * ?")]
    [InlineData("30 3 * * *", "0 30 3 * * ?")]
    [InlineData("0 1 * * 0", "0 0 1 ? * 1")]
    public void Converts_unix_cron_to_quartz(string unix, string quartz)
        => DependencyInjection.ToQuartzCron(unix).Should().Be(quartz);
}
