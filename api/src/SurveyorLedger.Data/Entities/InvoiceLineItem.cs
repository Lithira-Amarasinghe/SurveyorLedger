namespace SurveyorLedger.Data.Entities;

public class InvoiceLineItem
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? MilestoneId { get; set; }
    public Guid? QuotationLineId { get; set; }
}
