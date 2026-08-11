namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestTargetUpdateRequest
{
    public string? TargetRole { get; set; }
    public Guid? TargetUserId { get; set; }
}
