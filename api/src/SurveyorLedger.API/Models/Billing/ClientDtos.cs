using SurveyorLedger.API.Models.Land;

namespace SurveyorLedger.API.Models.Billing;

public class ClientRequest
{
    public string Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public AddressDto? Address { get; set; }
}

public class ClientResponse
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } // FirstName + " " + LastName, trimmed
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public AddressDto Address { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ClientBalanceResponse
{
    public Guid ClientId { get; set; }
    public decimal OutstandingBalance { get; set; }
}
