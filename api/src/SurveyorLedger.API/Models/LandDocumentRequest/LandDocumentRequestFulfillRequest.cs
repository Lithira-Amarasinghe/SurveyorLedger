using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SurveyorLedger.API.Models.LandDocumentRequest;

public class LandDocumentRequestFulfillRequest
{
    [Required(ErrorMessage = "At least one file is required.")]
    [MinLength(1, ErrorMessage = "At least one file is required.")]
    public required List<IFormFile> Files { get; set; }

    [Required(ErrorMessage = "BatchId is required.")]
    public required Guid BatchId { get; set; }

    public string? DisplayFileName { get; set; }
}
