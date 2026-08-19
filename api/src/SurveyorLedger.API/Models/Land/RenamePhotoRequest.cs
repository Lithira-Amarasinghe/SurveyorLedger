using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

public class RenamePhotoRequest
{
    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;
}
