namespace SurveyorLedger.API.Models.Milestone;

public class PaymentRequirementDto
{
    public required string TargetStatus { get; set; }
    public required string RequiredState { get; set; }
}

public class SetPaymentRequirementsRequest
{
    public List<PaymentRequirementDto> Requirements { get; set; } = new();
}

public class MilestonePaymentStatusResponse
{
    public decimal? Amount { get; set; }
    public Guid? LinkedInvoiceId { get; set; }
    public string? LinkedInvoiceNumber { get; set; }
    public string? InvoiceStatus { get; set; }
    public string? NextGate { get; set; }
}
