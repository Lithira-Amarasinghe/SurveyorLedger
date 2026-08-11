using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Milestone;

public class MilestoneStatusRequest
{
    [Required(ErrorMessage = "Status is required.")]
    [RegularExpression("^(Pending|InProgress|Completed)$",
        ErrorMessage = "Status must be Pending, InProgress, or Completed.")]
    public required string Status { get; set; }
}
