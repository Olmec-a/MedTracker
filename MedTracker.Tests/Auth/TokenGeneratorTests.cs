using FluentAssertions;
using MedTracker.Infrastructure.Services;
using Xunit;

namespace MedTracker.Tests.Auth;

public class TokenGeneratorTests
{
    private readonly TokenGenerator _sut = new();

    [Fact]
    public void GenerateToken_ReturnsBothPlaintextAndHash_AndTheyDiffer()
    {
        var (plain, hash) = _sut.GenerateToken();

        plain.Should().NotBeNullOrEmpty();
        hash.Should().NotBeNullOrEmpty();
        plain.Should().NotBe(hash, "plaintext отдаётся юзеру, hash хранится в БД");
    }

    [Fact]
    public void GenerateToken_ReturnsDifferentTokensEachCall()
    {
        var tokens = Enumerable.Range(0, 100)
            .Select(_ => _sut.GenerateToken().Plaintext)
            .ToHashSet();

        tokens.Should().HaveCount(100, "32 байта рандома → коллизии на 100 семплах исключены");
    }

    [Fact]
    public void GenerateToken_PlaintextIsUrlSafe()
    {
        // Base64Url не должен содержать +, /, = — иначе ломается в URL без экранирования
        for (int i = 0; i < 50; i++)
        {
            var (plain, _) = _sut.GenerateToken();
            plain.Should().NotContain("+");
            plain.Should().NotContain("/");
            plain.Should().NotContain("=");
        }
    }

    [Fact]
    public void GenerateToken_HashIsUppercaseHex()
    {
        var (_, hash) = _sut.GenerateToken();

        hash.Should().MatchRegex("^[0-9A-F]+$", "Convert.ToHexString даёт uppercase hex");
        hash.Length.Should().Be(64, "SHA-256 → 32 байта → 64 hex-символа");
    }

    [Fact]
    public void Verify_CorrectPlaintext_ReturnsTrue()
    {
        var (plain, hash) = _sut.GenerateToken();

        _sut.Verify(plain, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPlaintext_ReturnsFalse()
    {
        var (_, hash) = _sut.GenerateToken();

        _sut.Verify("attacker-guess", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "valid-hash")]
    [InlineData("valid-token", "")]
    [InlineData(null, "valid-hash")]
    [InlineData("valid-token", null)]
    public void Verify_NullOrEmptyInputs_ReturnsFalse(string? plaintext, string? hash)
    {
        _sut.Verify(plaintext!, hash!).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedHash_ReturnsFalse()
    {
        var (plain, hash) = _sut.GenerateToken();

        // Меняем один символ в хэше — Verify должен вернуть false
        var chars = hash.ToCharArray();
        chars[0] = chars[0] == 'A' ? 'B' : 'A';
        var tamperedHash = new string(chars);

        _sut.Verify(plain, tamperedHash).Should().BeFalse();
    }
}