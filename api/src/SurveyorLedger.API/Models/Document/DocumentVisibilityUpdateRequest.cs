using System.ComponentModel.DataAnnotations;
using SurveyorLedger.Core;

namespace SurveyorLedger.API.Models.Document;

public class DocumentVisibilityUpdateRequest
{
    [Required(ErrorMessage = "Visibility is required.")]
    public required DocumentVisibility Visibility { get; set; }
}
