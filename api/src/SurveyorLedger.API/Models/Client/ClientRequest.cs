using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Client;

/// <summary>
/// Request model for creating a bare client during a call - just a name and phone
/// number. No email/password yet; those come later via an invite once known.
/// </summary>
public class ClientRequest
{
    [Required(ErrorMessage = "FirstName is required.")]
    [StringLength(100, MinimumLength = 1)]
    public required string FirstName { get; set; }

    [Required(ErrorMessage = "LastName is required.")]
    [StringLength(100, MinimumLength = 1)]
    public required string LastName { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }
}
