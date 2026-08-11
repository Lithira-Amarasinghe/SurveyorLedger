using System.ComponentModel.DataAnnotations;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestCreateRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 1)]
    public required string Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Category is required.")]
    public required DocumentCategory Category { get; set; }
}
