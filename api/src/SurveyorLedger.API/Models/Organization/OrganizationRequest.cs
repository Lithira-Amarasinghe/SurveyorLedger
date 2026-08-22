using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Organization;

public class OrganizationRequest
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 255 characters.")]
    public required string Name { get; set; }
}
