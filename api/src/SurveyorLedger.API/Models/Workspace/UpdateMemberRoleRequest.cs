using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Workspace;

public class MemberRoleRequest
{
    // Which role names are actually valid here is a backend decision, driven by the
    // RoleScopes table (see WorkspaceService.AddMemberRoleAsync) - not a hardcoded list here
    // that has to be kept in sync by hand every time a role's scope changes.
    [Required(ErrorMessage = "Role is required.")]
    [StringLength(50, MinimumLength = 1)]
    public required string Role { get; set; }
}
