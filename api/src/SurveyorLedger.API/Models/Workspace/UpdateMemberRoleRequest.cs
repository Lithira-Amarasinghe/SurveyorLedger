using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Workspace;

public class UpdateMemberRoleRequest
{
    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression("^(Admin|Surveyor|Member)$", ErrorMessage = "Role must be Admin, Surveyor, or Member.")]
    public required string Role { get; set; }
}
