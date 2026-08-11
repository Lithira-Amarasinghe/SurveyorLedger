using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Milestone;

/// <summary>Full ordered list of a job's milestone ids, in the desired new order.</summary>
public class MilestoneReorderRequest
{
    [Required(ErrorMessage = "MilestoneIds is required.")]
    [MinLength(1, ErrorMessage = "MilestoneIds must not be empty.")]
    public required List<Guid> MilestoneIds { get; set; }
}
