using System.ComponentModel.DataAnnotations;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.LandDocumentRequest;

public class LandDocumentRequestCreateRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 1)]
    public required string Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Category is required.")]
    public required DocumentCategory Category { get; set; }

    public string? TargetRole { get; set; }
}
