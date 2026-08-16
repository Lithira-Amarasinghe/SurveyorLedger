namespace SurveyorLedger.Data.Entities;

/// <summary>
/// A payout to a staff member for work on a Job (salary/commission/bonus/profit
/// share). Amount is always manually entered - no percentage-of-revenue
/// auto-calculation. Tenant isolation transitive through JobId, same as Expense.
/// </summary>
public class StaffPayment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid JobId { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidDate { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Job Job { get; set; }
    public Person User { get; set; }
    public Person RecordedByUser { get; set; }
}
