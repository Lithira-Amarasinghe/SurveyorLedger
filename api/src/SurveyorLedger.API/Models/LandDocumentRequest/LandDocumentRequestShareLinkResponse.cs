namespace SurveyorLedger.API.Models.LandDocumentRequest;

public class LandDocumentRequestShareLinkResponse
{
    public required string Token { get; set; }
    public required DateTime ExpiresAt { get; set; }
}
