using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

public class LandSurveyRequest
{
    [Required(ErrorMessage = "SurveyPlanNumber is required.")]
    [StringLength(100)]
    public required string SurveyPlanNumber { get; set; }

    [Required(ErrorMessage = "SurveyDate is required.")]
    public required DateTime SurveyDate { get; set; }

    [StringLength(200)]
    public string? SurveyedByName { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}
