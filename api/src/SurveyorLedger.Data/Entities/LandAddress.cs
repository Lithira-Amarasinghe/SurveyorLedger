namespace SurveyorLedger.Data.Entities;

/// <summary>
/// EF Core owned type - Sri Lankan land administrative-division address, distinct from
/// the generic Address type Person/User use (Street/City don't apply to a land parcel,
/// and a mailing address doesn't need Grama Niladhari/Divisional Secretariat divisions).
/// English name where one exists. All fields optional.
/// </summary>
public class LandAddress
{
    public string? Village { get; set; }
    public string? GramaNiladhariDivision { get; set; }
    public string? DivisionalSecretariat { get; set; }
    public string? PradeshiyaSabha { get; set; }
    public string? Korale { get; set; }
    public string? Hatpattu { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
}
