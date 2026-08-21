namespace SurveyorLedger.API.Models.Workspace;

public class LetterheadRequest
{
    public string? CompanyName { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? RegistrationNumber { get; set; }
}

public class LetterheadResponse
{
    public string? CompanyName { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? RegistrationNumber { get; set; }
    public bool HasLogo { get; set; }
}
