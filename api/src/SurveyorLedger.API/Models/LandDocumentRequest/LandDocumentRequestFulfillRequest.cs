using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SurveyorLedger.API.Models.LandDocumentRequest;

public class LandDocumentRequestFulfillRequest
{
    [Required(ErrorMessage = "File is required.")]
    public required IFormFile File { get; set; }

    public string? DisplayFileName { get; set; }
}
