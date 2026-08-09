using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Job;

public class JobStatusRequest
{
    [Required(ErrorMessage = "Status is required.")]
    [RegularExpression("^(Draft|Scheduled|InProgress|Completed|Cancelled)$",
        ErrorMessage = "Status must be Draft, Scheduled, InProgress, Completed, or Cancelled.")]
    public required string Status { get; set; }
}
