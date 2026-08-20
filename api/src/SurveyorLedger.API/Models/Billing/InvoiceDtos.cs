namespace SurveyorLedger.API.Models.Billing;

public class InvoiceRequest
{
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Status { get; set; }
    public List<InstallmentDto> Installments { get; set; } = new();
}

public class InstallmentDto
{
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
}

public class InstallmentResponse
{
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; }
}

public class SendInvoiceRequest
{
    public List<Guid> RecipientPersonIds { get; set; } = new();
}

public class PaymentRequest
{
    public decimal Amount { get; set; }
    public string Method { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ReferenceNumber { get; set; }
}

public class PaymentResponse
{
    public Guid PaymentId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ReferenceNumber { get; set; }
    public bool HasProofFile { get; set; }
    public string ReceiptNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InvoiceResponse
{
    public Guid InvoiceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid JobId { get; set; }
    public string Number { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public string Status { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public int DaysOverdue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<InstallmentResponse> Installments { get; set; } = new();
}
