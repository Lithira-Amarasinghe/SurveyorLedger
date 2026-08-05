using Azure;
using Azure.Communication.Email;
using SurveyorLedger.Core;
using SurveyorLedger.Core.Exceptions;

namespace SurveyorLedger.API.Services;

public interface IEmailService
{
    Task SendVerificationOtpAsync(string email, string otpCode);
    Task SendWelcomeEmailAsync(string email, string firstName);
}

public class EmailService : IEmailService
{
    private readonly EmailClient _emailClient;
    private readonly string _senderEmail;
    private readonly ILogger<EmailService> _logger;

    public EmailService(EmailClient emailClient, IConfiguration config, ILogger<EmailService> logger)
    {
        _emailClient = emailClient;
        _senderEmail = config["AzureCommunicationServices:SenderEmail"]!;
        _logger = logger;
    }

    public async Task SendVerificationOtpAsync(string email, string otpCode)
    {
        var subject = "Your OTP Code";
        var body = $"Your verification code is: {otpCode}. It expires in 10 minutes.";
        await SendEmailAsync(email, subject, body);
    }

    public async Task SendWelcomeEmailAsync(string email, string firstName)
    {
        var subject = "Welcome to SurveyorLedger";
        var body = $"Hello {firstName}, welcome to SurveyorLedger!";
        await SendEmailAsync(email, subject, body);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        try
        {
            var message = new EmailMessage(
                senderAddress: _senderEmail,
                recipients: new EmailRecipients(new[] { new EmailAddress(toEmail) }),
                content: new EmailContent(subject)
                {
                    PlainText = body
                });

            await _emailClient.SendAsync(WaitUntil.Completed, message);
            _logger.LogInformation("Email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            throw new AppException(Constants.ErrorCodes.EmailSendFailed, "Failed to send email");
        }
    }
}
