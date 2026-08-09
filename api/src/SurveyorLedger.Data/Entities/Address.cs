namespace SurveyorLedger.Data.Entities;

/// <summary>
/// EF Core owned type - no separate table, columns embedded on the owner (User, Land).
/// Structured for searchability without the join cost of a real Address entity.
/// </summary>
public class Address
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}
