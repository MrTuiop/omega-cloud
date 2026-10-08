using OmegaCloud.Infrastructure.Security;

namespace OmegaCloud.Tests.Security;

public class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ShouldGenerateValidArgon2idFormat()
    {
        var password = "SuperSecretPassword123!";

        var hash = _hasher.HashPassword(password);

        Assert.NotNull(hash);
        Assert.StartsWith("$argon2id$", hash);
        Assert.True(_hasher.VerifyPassword(password, hash));
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ShouldReturnFalse()
    {
        var password = "CorrectPassword123!";
        var wrongPassword = "WrongPassword!";

        var hash = _hasher.HashPassword(password);

        Assert.False(_hasher.VerifyPassword(wrongPassword, hash));
    }

    [Fact]
    public void HashPassword_ShouldGenerateDifferentHashesForSamePasswordDueToSalt()
    {
        var password = "SamePasswordEveryTime";

        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
        Assert.True(_hasher.VerifyPassword(password, hash1));
        Assert.True(_hasher.VerifyPassword(password, hash2));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void HashPassword_WithEmptyPassword_ShouldThrowArgumentException(string emptyPassword)
    {
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(emptyPassword));
    }

    [Theory]
    [InlineData(null, "$argon2id$somehash")]
    [InlineData("password", null)]
    [InlineData("password", "")]
    [InlineData("password", "invalid_hash_string")]
    public void VerifyPassword_WithInvalidInputs_ShouldReturnFalse(string? password, string? hash)
    {
        var result = _hasher.VerifyPassword(password!, hash!);
        Assert.False(result);
    }
}
