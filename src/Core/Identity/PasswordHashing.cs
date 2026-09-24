using System.Security.Cryptography;

namespace Core.Identity;

/// <summary>
/// The password provider's hash (4.2): PBKDF2-HMAC-SHA512, 210,000 iterations, a 16-byte random salt and a
/// 64-byte derived key, stored as <c>pbkdf2-sha512$iterations$salt$key</c> (base64). Verification compares in
/// constant time, and a missing account is verified against <see cref="DummyHash"/> so both paths do the same
/// work (T3.5: identical responses in text and approximate timing).
/// </summary>
public static class PasswordHashing
{
    private const string Algorithm = "pbkdf2-sha512";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 64;

    private static readonly Lazy<string> Dummy = new(() => Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));

    /// <summary>A valid hash of a random secret no one knows: the work a missing account is verified against.</summary>
    public static string DummyHash => Dummy.Value;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, KeySize);
        return $"{Algorithm}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    /// <summary>
    /// True only for a well-formed hash of <paramref name="password"/>. A malformed stored value fails, after the
    /// same work as a real verification.
    /// </summary>
    public static bool Verify(string password, string stored)
    {
        if (!TryParse(stored, out var salt, out var expected))
        {
            Verify(password, DummyHash);
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, KeySize);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // Only this algorithm and iteration count are accepted: a stored value cannot choose its own cost.
    private static bool TryParse(string stored, out byte[] salt, out byte[] key)
    {
        salt = key = [];
        var parts = stored.Split('$');
        if (parts is not [Algorithm, var iterations, var salt64, var key64] || iterations != Iterations.ToString())
            return false;
        try
        {
            salt = Convert.FromBase64String(salt64);
            key = Convert.FromBase64String(key64);
        }
        catch (FormatException)
        {
            return false;
        }
        return salt.Length == SaltSize && key.Length == KeySize;
    }
}
