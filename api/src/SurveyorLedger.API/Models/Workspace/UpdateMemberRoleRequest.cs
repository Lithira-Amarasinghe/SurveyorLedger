using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Workspace;

public class MemberRoleRequest
{
    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression("^(Admin|Surveyor|Member|WorkspaceMember)$", ErrorMessage = "Role must be Admin, Surveyor, Member, or WorkspaceMember.")]
    public required string Role { get; set; }
}
