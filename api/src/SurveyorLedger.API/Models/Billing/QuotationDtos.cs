namespace SurveyorLedger.API.Models.Billing;

public class LineItemDto
{
    public Guid? Id { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? MilestoneId { get; set; }
    public Guid? QuotationLineId { get; set; }
}

public class QuotationRequest
{
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? Status { get; set; }
}

public class SendQuotationRequest
{
    public List<Guid> RecipientPersonIds { get; set; } = new();
}

public class QuotationLineItemResponse
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? MilestoneId { get; set; }
    public decimal InvoicedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
}

public class QuotationResponse
{
    public Guid QuotationId { get; set; }
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public string Number { get; set; }
    public List<QuotationLineItemResponse> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int RevisionNumber { get; set; }
    public decimal InvoicedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
