using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.DocumentRequest;

public class DocumentRequestFulfillRequest
{
    [Required(ErrorMessage = "File is required.")]
    public required IFormFile File { get; set; }

    [Required(ErrorMessage = "Visibility is required.")]
    public required DocumentVisibility Visibility { get; set; }

    public string? DisplayFileName { get; set; }
}
