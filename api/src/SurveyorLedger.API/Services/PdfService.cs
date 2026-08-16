using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

public interface IPdfService
{
    byte[] GenerateInvoicePdf(Invoice invoice, (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) totals);
    byte[] GenerateQuotationPdf(Quotation quotation);
}

/// <summary>
/// Functional line-item table, not a styled template - see spec's "Out of scope".
/// QuestPDF Community license is set once via QuestPDF.Settings in Program.cs.
/// </summary>
public class PdfService : IPdfService
{
    static PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateInvoicePdf(Invoice invoice, (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) totals)
    {
        var subtotal = invoice.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Header().Text($"Invoice {invoice.Number}").FontSize(18).Bold();
                page.Content().Column(col =>
                {
                    col.Item().Text($"Billed to: {invoice.Client.FirstName} {invoice.Client.LastName}");
                    col.Item().Text($"Status: {invoice.Status}");
                    if (invoice.DueDate.HasValue)
                        col.Item().Text($"Due: {invoice.DueDate.Value:yyyy-MM-dd}");
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                        table.Header(h =>
                        {
                            h.Cell().Text("Description").Bold();
                            h.Cell().Text("Qty").Bold();
                            h.Cell().Text("Unit Price").Bold();
                            h.Cell().Text("Amount").Bold();
                        });
                        foreach (var li in invoice.LineItems)
                        {
                            table.Cell().Text(li.Description);
                            table.Cell().Text(li.Quantity.ToString("0.##"));
                            table.Cell().Text(li.UnitPrice.ToString("0.00"));
                            table.Cell().Text((li.Quantity * li.UnitPrice).ToString("0.00"));
                        }
                    });
                    col.Item().PaddingTop(10).AlignRight().Text($"Subtotal: {subtotal:0.00}");
                    col.Item().AlignRight().Text($"Tax ({invoice.TaxRatePercent}%): {(subtotal * invoice.TaxRatePercent / 100m):0.00}");
                    col.Item().AlignRight().Text($"Discount: -{invoice.DiscountAmount:0.00}");
                    col.Item().AlignRight().Text($"Total: {totals.Total:0.00}").Bold();
                    col.Item().AlignRight().Text($"Paid: {totals.AmountPaid:0.00}");
                    col.Item().AlignRight().Text($"Balance: {totals.Balance:0.00}").Bold();
                });
            });
        }).GeneratePdf();
    }

    public byte[] GenerateQuotationPdf(Quotation quotation)
    {
        var subtotal = quotation.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        var tax = subtotal * quotation.TaxRatePercent / 100m;
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Header().Text($"Quotation {quotation.Number}").FontSize(18).Bold();
                page.Content().Column(col =>
                {
                    col.Item().Text($"Prepared for: {quotation.Client.FirstName} {quotation.Client.LastName}");
                    col.Item().Text($"Status: {quotation.Status}");
                    if (quotation.ValidUntil.HasValue)
                        col.Item().Text($"Valid until: {quotation.ValidUntil.Value:yyyy-MM-dd}");
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                        table.Header(h =>
                        {
                            h.Cell().Text("Description").Bold();
                            h.Cell().Text("Qty").Bold();
                            h.Cell().Text("Unit Price").Bold();
                            h.Cell().Text("Amount").Bold();
                        });
                        foreach (var li in quotation.LineItems)
                        {
                            table.Cell().Text(li.Description);
                            table.Cell().Text(li.Quantity.ToString("0.##"));
                            table.Cell().Text(li.UnitPrice.ToString("0.00"));
                            table.Cell().Text((li.Quantity * li.UnitPrice).ToString("0.00"));
                        }
                    });
                    col.Item().PaddingTop(10).AlignRight().Text($"Subtotal: {subtotal:0.00}");
                    col.Item().AlignRight().Text($"Tax ({quotation.TaxRatePercent}%): {tax:0.00}");
                    col.Item().AlignRight().Text($"Total: {(subtotal + tax):0.00}").Bold();
                });
            });
        }).GeneratePdf();
    }
}
