namespace SurveyorLedger.API.Models.StaffPayment;

public class StaffPaymentRequest
{
    public Guid UserId { get; set; }
    public string Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidDate { get; set; }
    public string? Notes { get; set; }
}

public class StaffPaymentResponse
{
    public Guid StaffPaymentId { get; set; }
    public Guid JobId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; }
    public string Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
