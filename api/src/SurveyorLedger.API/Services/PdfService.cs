using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SurveyorLedger.Data.Entities;

namespace SurveyorLedger.API.Services;

/// <summary>Loaded once by the caller (InvoiceService/QuotationService already have the
/// workspace + file storage on hand) and passed in - PdfService itself stays
/// storage-agnostic, same as how `invoice.Job` is passed in via the entity, not re-fetched.</summary>
public record PdfLetterhead(string? CompanyName, string? Address, string? Phone, string? Email, string? RegistrationNumber, byte[]? LogoBytes);

public interface IPdfService
{
    byte[] GenerateInvoicePdf(Invoice invoice, (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) totals, PdfLetterhead? letterhead = null);
    byte[] GenerateQuotationPdf(Quotation quotation, PdfLetterhead? letterhead = null);
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

    public byte[] GenerateInvoicePdf(Invoice invoice, (decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue) totals, PdfLetterhead? letterhead = null)
    {
        var subtotal = invoice.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Header().Column(col =>
                {
                    RenderLetterhead(col, letterhead);
                    col.Item().Text($"Invoice {invoice.Number}").FontSize(18).Bold();
                });
                page.Content().Column(col =>
                {
                    if (invoice.Job != null)
                        col.Item().Text($"Job: {invoice.Job.JobNumber} - {invoice.Job.Title}");
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

    public byte[] GenerateQuotationPdf(Quotation quotation, PdfLetterhead? letterhead = null)
    {
        var subtotal = quotation.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        var tax = subtotal * quotation.TaxRatePercent / 100m;
        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Header().Column(col =>
                {
                    RenderLetterhead(col, letterhead);
                    col.Item().Text($"Quotation {quotation.Number}").FontSize(18).Bold();
                });
                page.Content().Column(col =>
                {
                    if (quotation.Job != null)
                        col.Item().Text($"Job: {quotation.Job.JobNumber} - {quotation.Job.Title}");
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

    /// <summary>No-ops entirely when nothing is set, so a workspace without a letterhead
    /// renders exactly as before this feature existed.</summary>
    private static void RenderLetterhead(ColumnDescriptor col, PdfLetterhead? letterhead)
    {
        if (letterhead == null) return;
        var hasText = letterhead.CompanyName != null || letterhead.Address != null || letterhead.Phone != null || letterhead.Email != null || letterhead.RegistrationNumber != null;
        if (!hasText && letterhead.LogoBytes == null) return;

        col.Item().PaddingBottom(10).Row(row =>
        {
            if (letterhead.LogoBytes != null)
                row.ConstantItem(50).Height(50).Image(letterhead.LogoBytes).FitArea();

            row.RelativeItem().PaddingLeft(letterhead.LogoBytes != null ? 10 : 0).Column(text =>
            {
                if (letterhead.CompanyName != null)
                    text.Item().Text(letterhead.CompanyName).FontSize(14).Bold();
                if (letterhead.Address != null)
                    text.Item().Text(letterhead.Address).FontSize(9);
                var contact = string.Join(" · ", new[] { letterhead.Phone, letterhead.Email }.Where(s => s != null));
                if (contact.Length > 0)
                    text.Item().Text(contact).FontSize(9);
                if (letterhead.RegistrationNumber != null)
                    text.Item().Text($"Reg. {letterhead.RegistrationNumber}").FontSize(8);
            });
        });
    }
}
