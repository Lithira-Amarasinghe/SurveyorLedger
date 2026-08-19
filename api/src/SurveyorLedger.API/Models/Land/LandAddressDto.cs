using System.ComponentModel.DataAnnotations;

namespace SurveyorLedger.API.Models.Land;

/// <summary>
/// Sri Lankan land administrative-division address - mirrors the LandAddress owned type.
/// Distinct from the generic Address type Person/User use.
/// </summary>
public class LandAddressDto
{
    [StringLength(150)]
    public string? Village { get; set; }

    [StringLength(150)]
    public string? GramaNiladhariDivision { get; set; }

    [StringLength(150)]
    public string? DivisionalSecretariat { get; set; }

    [StringLength(150)]
    public string? PradeshiyaSabha { get; set; }

    [StringLength(150)]
    public string? Korale { get; set; }

    [StringLength(150)]
    public string? Hatpattu { get; set; }

    [StringLength(150)]
    public string? District { get; set; }

    [StringLength(150)]
    public string? Province { get; set; }
}
