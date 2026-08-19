using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestFulfillRequest
{
    [Required(ErrorMessage = "At least one file is required.")]
    [MinLength(1, ErrorMessage = "At least one file is required.")]
    public required List<IFormFile> Files { get; set; }

    [Required(ErrorMessage = "BatchId is required.")]
    public required Guid BatchId { get; set; }

    [Required(ErrorMessage = "Visibility is required.")]
    public required DocumentVisibility Visibility { get; set; }

    public string? DisplayFileName { get; set; }
}
