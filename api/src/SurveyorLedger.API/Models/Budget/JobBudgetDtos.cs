namespace SurveyorLedger.API.Models.Budget;

public class JobBudgetRequest
{
    public decimal EstimatedFee { get; set; }
    public decimal EstimatedCost { get; set; }
}

public class JobBudgetResponse
{
    public Guid JobId { get; set; }
    public decimal EstimatedFee { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal ExpectedProfit { get; set; }
    public string UpdatedByName { get; set; }
    public DateTime UpdatedAt { get; set; }
}
