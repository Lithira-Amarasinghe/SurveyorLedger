using Xunit;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>
/// Minimal unit tests for authentication service.
/// Full integration tests with real database in separate integration test project.
/// </summary>
public class AuthServiceTests
{
    /// <summary>
    /// Tests that PasswordService dependency works correctly for hashing.
    /// </summary>
    [Fact]
    public void PasswordService_Integration_HashesAndVerifies()
    {
        // Arrange
        var passwordService = new PasswordService();
        var password = "TestPassword123!";

        // Act
        var hash = passwordService.HashPassword(password);
        var isValid = passwordService.VerifyPassword(password, hash);

        // Assert
        Assert.NotEmpty(hash);
        Assert.True(isValid);
    }

    /// <summary>
    /// Tests that PasswordService rejects incorrect passwords.
    /// </summary>
    [Fact]
    public void PasswordService_Verification_RejectsWrongPassword()
    {
        // Arrange
        var passwordService = new PasswordService();
        var correctPassword = "TestPassword123!";
        var wrongPassword = "WrongPassword";
        var hash = passwordService.HashPassword(correctPassword);

        // Act
        var isValid = passwordService.VerifyPassword(wrongPassword, hash);

        // Assert
        Assert.False(isValid);
    }
}
