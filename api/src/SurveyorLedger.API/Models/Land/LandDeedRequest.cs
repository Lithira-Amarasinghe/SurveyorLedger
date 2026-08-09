using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

public class LandDeedRequest
{
    [Required(ErrorMessage = "DeedNumber is required.")]
    [StringLength(100)]
    public required string DeedNumber { get; set; }

    [Required(ErrorMessage = "IssuedDate is required.")]
    public required DateTime IssuedDate { get; set; }

    /// <summary>
    /// Defaults true. Setting a new deed IsCurrent supersedes any prior current deed
    /// for the same Land (handled in the service, not here).
    /// </summary>
    public bool IsCurrent { get; set; } = true;

    [StringLength(2000)]
    public string? Notes { get; set; }
}
