using SurveyorLedger.API.Services;
using SurveyorLedger.Data.Entities;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class PdfServiceTests
{
    [Fact]
    public void GenerateInvoicePdf_ProducesNonEmptyPdfBytes()
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            Number = "INV-0001",
            WorkspaceId = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            Status = "Sent",
            LineItems = new List<InvoiceLineItem> { new() { Id = Guid.NewGuid(), Description = "Survey work", Quantity = 2, UnitPrice = 5000m } },
            TaxRatePercent = 10,
            DiscountAmount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var svc = new PdfService();

        var bytes = svc.GenerateInvoicePdf(invoice, (11000m, 0m, 11000m, false, 0));

        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'%', bytes[0]);
    }
}
