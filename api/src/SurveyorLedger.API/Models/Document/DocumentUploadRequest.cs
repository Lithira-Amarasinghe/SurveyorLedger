using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.Document;

/// <summary>
/// Bound from multipart/form-data - File is the upload, the rest are form fields.
/// </summary>
public class DocumentUploadRequest
{
    [Required(ErrorMessage = "File is required.")]
    public required IFormFile File { get; set; }

    [Required(ErrorMessage = "Category is required.")]
    public required DocumentCategory Category { get; set; }

    [Required(ErrorMessage = "Visibility is required.")]
    public required DocumentVisibility Visibility { get; set; }

    [StringLength(260)]
    public string? DisplayFileName { get; set; }
}
