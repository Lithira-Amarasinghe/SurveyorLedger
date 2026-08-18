namespace SurveyorLedger.Data.Entities;

/// <summary>
/// 1:1 with Job via JobId as both PK and FK - deliberately not columns on Job (finance
/// data kept out of the core job record). No row exists until an Admin sets one.
/// </summary>
public class JobBudget
{
    public Guid JobId { get; set; }
    public decimal EstimatedFee { get; set; }
    public decimal EstimatedCost { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Job Job { get; set; }
    public Person UpdatedByPerson { get; set; }
}
