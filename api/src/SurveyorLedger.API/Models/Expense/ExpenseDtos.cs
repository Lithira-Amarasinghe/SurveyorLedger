namespace SurveyorLedger.API.Models.Expense;

public class ExpenseRequest
{
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime IncurredDate { get; set; }
    public Guid? PayeeId { get; set; }
    public string? PayeeType { get; set; }
}

public class ExpenseResponse
{
    public Guid ExpenseId { get; set; }
    public Guid JobId { get; set; }
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime IncurredDate { get; set; }
    public bool HasReceipt { get; set; }
    public Guid? PayeeId { get; set; }
    public string? PayeeName { get; set; }
    public string? PayeeType { get; set; }
    public string RecordedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}
