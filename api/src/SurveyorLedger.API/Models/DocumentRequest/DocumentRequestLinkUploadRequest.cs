using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestLinkUploadRequest
{
    [Required(ErrorMessage = "At least one file is required.")]
    [MinLength(1, ErrorMessage = "At least one file is required.")]
    public required List<IFormFile> Files { get; set; }

    public string? DisplayFileName { get; set; }
}
