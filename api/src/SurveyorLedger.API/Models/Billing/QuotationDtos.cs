namespace SurveyorLedger.API.Models.Billing;

public class LineItemDto
{
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class QuotationRequest
{
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? Status { get; set; }
}

public class ConvertQuotationRequest
{
    public DateTime? DueDate { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class QuotationResponse
{
    public Guid QuotationId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? JobId { get; set; }
    public string Number { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int RevisionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
