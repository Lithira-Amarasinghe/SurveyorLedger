namespace SurveyorLedger.Data.Entities;

/// <summary>
/// One user-defined gate: Milestone cannot enter TargetStatus until the milestone's
/// linked invoice (via a tagged InvoiceLineItem) reaches RequiredState. No fixed pair
/// of gates - a milestone can have zero, one, or several of these, on any of its
/// statuses. RequiredState is "Invoiced" | "PartiallyPaid" | "FullyPaid".
/// </summary>
public class MilestonePaymentRequirement
{
    public Guid Id { get; set; }
    public string TargetStatus { get; set; }
    public string RequiredState { get; set; }
}
