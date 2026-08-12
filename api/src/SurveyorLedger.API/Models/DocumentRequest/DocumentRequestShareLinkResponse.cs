namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestShareLinkResponse
{
    public required string Token { get; set; }
    public required DateTime ExpiresAt { get; set; }
}
