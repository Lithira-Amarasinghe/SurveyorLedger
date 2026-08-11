using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Workspace;

public class UpdateMemberRoleRequest
{
    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression("^(Admin|Surveyor|Client)$", ErrorMessage = "Role must be Admin, Surveyor, or Client.")]
    public required string Role { get; set; }
}
