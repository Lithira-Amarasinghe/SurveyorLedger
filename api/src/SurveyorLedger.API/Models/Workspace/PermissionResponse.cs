namespace SurveyorLedger.API.Models.Workspace;

public class PermissionResponse
{
    public required string Name { get; set; }
    public required string Resource { get; set; }
    public required string Action { get; set; }
    public required string Description { get; set; }
}
