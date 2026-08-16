using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>Test double - no real email provider is available in the integration test harness.</summary>
public class NoOpEmailService : IEmailService
{
    public Task SendVerificationOtpAsync(string email, string otpCode, int expirationMinutes) => Task.CompletedTask;
    public Task SendPasswordResetOtpAsync(string email, string otpCode, int expirationMinutes) => Task.CompletedTask;
    public Task SendWelcomeEmailAsync(string email, string firstName) => Task.CompletedTask;
    public Task SendInviteEmailAsync(string email, string workspaceName, string inviteUrl) => Task.CompletedTask;
    public Task SendBillingDocumentAsync(string email, string documentType, string documentNumber, string linkUrl, byte[] pdfBytes, string pdfFileName) => Task.CompletedTask;
}
