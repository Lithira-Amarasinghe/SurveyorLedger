using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Workspace;

public class UpdateMemberRoleRequest
{
    [Required(ErrorMessage = "Role is required.")]
    [RegularExpression("^(Admin|Manager|Surveyor|Client)$", ErrorMessage = "Role must be Admin, Manager, Surveyor, or Client.")]
    public required string Role { get; set; }
}
