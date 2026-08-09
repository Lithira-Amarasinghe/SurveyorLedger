using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

public class LandBoundaryRequest
{
    [Required(ErrorMessage = "Label is required.")]
    [StringLength(100)]
    public required string Label { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }
}
