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

public class LinkedInvoiceSummaryDto
{
    public Guid InvoiceId { get; set; }
    public required string Number { get; set; }
    public required string Status { get; set; }
}

public class LinkedQuotationSummaryDto
{
    public Guid QuotationId { get; set; }
    public required string Number { get; set; }
    public required string Status { get; set; }
}

public class MilestonePaymentStatusResponse
{
    public decimal? Amount { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal QuotedAmount { get; set; }
    public decimal InvoicedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal? RemainingAmount { get; set; }
    public List<LinkedInvoiceSummaryDto> LinkedInvoices { get; set; } = new();
    public List<LinkedQuotationSummaryDto> LinkedQuotations { get; set; } = new();
    public string? NextGate { get; set; }
}
