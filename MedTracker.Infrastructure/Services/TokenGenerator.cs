using System.Security.Cryptography;
using System.Text;
using MedTracker.Application.Interfaces;

namespace MedTracker.Infrastructure.Services;

/// <summary>
/// Генерирует 32-байтовые криптографически случайные токены.
/// Plaintext отправляется пользователю в письме, в БД хранится только SHA-256 хеш.
/// При проверке используется FixedTimeEquals, чтобы избежать timing-атак.
/// </summary>
public class TokenGenerator : ITokenGenerator
{
    public (string Plaintext, string Hash) GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        // Base64Url — безопасно вставлять в URL без экранирования
        var plaintext = Base64UrlEncode(bytes);
        var hash = ComputeHash(plaintext);
        return (plaintext, hash);
    }

    public bool Verify(string plaintext, string storedHash)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(storedHash))
            return false;

        var computed = ComputeHash(plaintext);

        // Convert обе строки к байтам и сравниваем за константное время
        var a = Encoding.UTF8.GetBytes(computed);
        var b = Encoding.UTF8.GetBytes(storedHash);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string ComputeHash(string plaintext)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(plaintext), hash);
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var s = Convert.ToBase64String(bytes);
        return s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}