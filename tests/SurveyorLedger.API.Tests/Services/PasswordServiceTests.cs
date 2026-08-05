using Xunit;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Tests.Services;

public class PasswordServiceTests
{
    [Fact]
    public void HashPassword_ProducesValidBcryptHash()
    {
        // Arrange
        var service = new PasswordService();
        var password = "MySecurePassword123!";

        // Act
        var hash = service.HashPassword(password);

        // Assert
        Assert.NotEmpty(hash);
        Assert.StartsWith("$2", hash);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var service = new PasswordService();
        var password = "MySecurePassword123!";
        var hash = service.HashPassword(password);

        // Act
        var result = service.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        // Arrange
        var service = new PasswordService();
        var correctPassword = "CorrectPassword";
        var wrongPassword = "WrongPassword";
        var hash = service.HashPassword(correctPassword);

        // Act
        var result = service.VerifyPassword(wrongPassword, hash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_InvalidHashFormat_ReturnsFalse()
    {
        // Arrange
        var service = new PasswordService();
        var password = "MyPassword";
        var invalidHash = "not_a_valid_bcrypt_hash";

        // Act
        var result = service.VerifyPassword(password, invalidHash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HashPassword_DifferentCallsProduceDifferentHashes()
    {
        // Arrange
        var service = new PasswordService();
        var password = "SamePassword123!";

        // Act
        var hash1 = service.HashPassword(password);
        var hash2 = service.HashPassword(password);

        // Assert
        Assert.NotEqual(hash1, hash2);
        // But both should verify correctly
        Assert.True(service.VerifyPassword(password, hash1));
        Assert.True(service.VerifyPassword(password, hash2));
    }
}
