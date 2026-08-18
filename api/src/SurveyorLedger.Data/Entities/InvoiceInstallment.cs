namespace SurveyorLedger.Data.Entities;

public class InvoiceInstallment
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
}
