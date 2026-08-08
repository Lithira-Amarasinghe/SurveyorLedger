namespace SurveyorLedger.API.Models.Workspace;

public class RoleResponse
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required List<PermissionResponse> Permissions { get; set; }
}
