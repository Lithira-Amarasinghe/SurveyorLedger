using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SurveyorLedger.API.Controllers;
using SurveyorLedger.API.Services;
using SurveyorLedger.API.Models.Auth;
using SurveyorLedger.API.Models.Responses;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Tests.Controllers;

/// <summary>
/// Minimal unit tests for authentication controller.
/// Tests controller response structure with mocked service dependencies.
/// </summary>
public class AuthControllerTests
{
    private AuthController CreateControllerWithMockedHttpContext(IAuthService authService)
    {
        var mockLogger = new Mock<ILogger<AuthController>>();
        var controller = new AuthController(authService, mockLogger.Object);

        // Mock HttpContext and Response for cookie handling
        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        var mockResponse = new Mock<HttpResponse>();
        var mockResponseCookies = new Mock<IResponseCookies>();

        mockResponse.Setup(r => r.Cookies).Returns(mockResponseCookies.Object);
        mockHttpContext.Setup(c => c.Request).Returns(mockRequest.Object);
        mockHttpContext.Setup(c => c.Response).Returns(mockResponse.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = mockHttpContext.Object };

        return controller;
    }

    [Fact]
    public async Task Register_ValidRequest_Returns200Ok()
    {
        // Arrange
        var mockAuthService = new Mock<IAuthService>();
        var controller = CreateControllerWithMockedHttpContext(mockAuthService.Object);

        var request = new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Password123!",
            FirstName = "Test",
            LastName = "User"
        };

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User",
            PasswordHash = "hashed",
            EmailVerified = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        mockAuthService
            .Setup(x => x.RegisterAsync(It.IsAny<RegisterRequest>()))
            .ReturnsAsync((user, "access_token", "refresh_token", 3600));

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
        var response = okResult.Value as ApiResponse<AuthResponse>;
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal("test@example.com", response.Data.Email);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200Ok()
    {
        // Arrange
        var mockAuthService = new Mock<IAuthService>();
        var controller = CreateControllerWithMockedHttpContext(mockAuthService.Object);

        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = "Password123!"
        };

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User",
            PasswordHash = "hashed",
            EmailVerified = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        mockAuthService
            .Setup(x => x.LoginAsync(It.IsAny<LoginRequest>()))
            .ReturnsAsync((user, "access_token", "refresh_token", 3600));

        // Act
        var result = await controller.Login(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
        var response = okResult.Value as ApiResponse<AuthResponse>;
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal("access_token", response.Data.AccessToken);
    }

    [Fact]
    public async Task VerifyOtp_ValidRequest_Returns200Ok()
    {
        // Arrange
        var mockAuthService = new Mock<IAuthService>();
        var controller = CreateControllerWithMockedHttpContext(mockAuthService.Object);

        var request = new VerifyOtpRequest
        {
            Email = "test@example.com",
            OtpCode = "123456"
        };

        mockAuthService
            .Setup(x => x.VerifyOtpAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await controller.VerifyOtp(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
        var response = okResult.Value as ApiResponse<object>;
        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task RefreshToken_MissingToken_Returns401Unauthorized()
    {
        // Arrange
        var mockAuthService = new Mock<IAuthService>();
        var mockLogger = new Mock<ILogger<AuthController>>();
        var controller = new AuthController(mockAuthService.Object, mockLogger.Object);

        // Mock HttpContext with proper Request.Cookies
        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        var mockRequestCookies = new Mock<IRequestCookieCollection>();

        // Return null for refreshToken cookie
        mockRequestCookies.Setup(c => c["refreshToken"]).Returns((string?)null);
        mockRequest.Setup(r => r.Cookies).Returns(mockRequestCookies.Object);
        mockHttpContext.Setup(c => c.Request).Returns(mockRequest.Object);

        controller.ControllerContext = new ControllerContext { HttpContext = mockHttpContext.Object };

        var request = new RefreshTokenRequest { RefreshToken = null };

        // Act
        var result = await controller.RefreshToken(request);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.NotNull(unauthorizedResult.Value);
    }
}
