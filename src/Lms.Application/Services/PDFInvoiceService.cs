using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Lms.Domain.Entities;

namespace Lms.Application.Services;

public interface IPDFInvoiceService
{
    Task<string> GeneratePdfInvoiceAsync(Invoice invoice, PaymentTransaction transaction, string learnerEmail, List<string> courseNames, decimal taxRate = 0.08m);
}

public class PDFInvoiceService : IPDFInvoiceService
{
    private readonly ILogger<PDFInvoiceService> _logger;
    private readonly string _invoicePath;

    public PDFInvoiceService(ILogger<PDFInvoiceService> logger)
    {
        _logger = logger;
        
        // Set invoice storage directory (App_Data/Invoices)
        var baseDirectory = AppContext.BaseDirectory;
        _invoicePath = Path.Combine(baseDirectory, "..", "..", "Lms.Web", "App_Data", "Invoices");
        
        // Create directory if it doesn't exist
        if (!Directory.Exists(_invoicePath))
        {
            Directory.CreateDirectory(_invoicePath);
            _logger.LogInformation($"Created invoice directory: {_invoicePath}");
        }
    }

    public async Task<string> GeneratePdfInvoiceAsync(Invoice invoice, PaymentTransaction transaction, string learnerEmail, List<string> courseNames, decimal taxRate = 0.08m)
    {
        try
        {
            // Calculate amounts
            var subtotal = transaction.Amount / (1 + taxRate);
            var taxAmount = transaction.Amount - subtotal;
            var filename = $"{invoice.InvoiceNumber.Replace("/", "-")}.pdf";
            var filepath = Path.Combine(_invoicePath, filename);

            // Generate PDF document
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);

                    page.Header().ShowOnce().Element(CompanyHeader);
                    
                    page.Content().Element(content =>
                    {
                        content.Column(column =>
                        {
                            // Invoice header section
                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Column(innerColumn =>
                                {
                                    innerColumn.Item().Text("INVOICE").Bold().FontSize(28).FontColor(Colors.Grey.Darken3);
                                    innerColumn.Item().Text(invoice.InvoiceNumber).FontSize(14).Italic().FontColor(Colors.Blue.Medium);
                                });

                                row.RelativeItem().AlignRight().Column(innerColumn =>
                                {
                                    innerColumn.Item().Text($"Issued: {invoice.IssuedAt:MMMM dd, yyyy}").FontSize(11);
                                    innerColumn.Item().Text($"Due: {invoice.IssuedAt.AddDays(30):MMMM dd, yyyy}").FontSize(11);
                                });
                            });

                            column.Item().PaddingVertical(20).LineHorizontal(1);

                            // Bill to section
                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Column(innerColumn =>
                                {
                                    innerColumn.Item().Text("BILL TO").Bold().FontSize(11).FontColor(Colors.Grey.Darken3);
                                    innerColumn.Item().Text(learnerEmail).FontSize(10);
                                });
                            });

                            column.Item().PaddingVertical(20);

                            // Items table
                            column.Item().Element(content => ComposeTable(content, courseNames, subtotal, taxAmount, transaction.Amount));
                        });
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Thank you for your purchase! ").FontSize(9);
                        text.Span("Page ").FontSize(9);
                        text.CurrentPageNumber().FontSize(9);
                    });
                });
            });

            // Write PDF to file
            await Task.Run(() => document.GeneratePdf(filepath));

            _logger.LogInformation($"PDF invoice generated: {filepath}");
            return filename;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating PDF invoice for {invoice.InvoiceNumber}");
            throw;
        }
    }

    private static void CompanyHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("Learning Management System").Bold().FontSize(16);
            column.Item().Text("www.example.com | support@example.com | (555) 123-4567").FontSize(9).FontColor(Colors.Grey.Medium);
        });
    }

    private static void ComposeTable(IContainer container, List<string> courseNames, decimal subtotal, decimal taxAmount, decimal total)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.RelativeColumn(1);
                columns.RelativeColumn(1);
                columns.RelativeColumn(1);
            });

            table.Header(header =>
            {
                header.Cell().Background(Colors.Blue.Medium).Padding(5).Text("Course/Item").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Medium).Padding(5).AlignRight().Text("Unit Price").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Medium).Padding(5).AlignRight().Text("Qty").FontColor(Colors.White).Bold();
                header.Cell().Background(Colors.Blue.Medium).Padding(5).AlignRight().Text("Amount").FontColor(Colors.White).Bold();
            });

            var itemPrice = courseNames.Count > 0 ? subtotal / courseNames.Count : 0;
            foreach (var courseName in courseNames)
            {
                table.Cell().Padding(5).Text(courseName);
                table.Cell().Padding(5).AlignRight().Text($"${itemPrice:F2}");
                table.Cell().Padding(5).AlignRight().Text("1");
                table.Cell().Padding(5).AlignRight().Text($"${itemPrice:F2}");
            }

            table.Cell().ColumnSpan(4).Height(10).Border(0);

            table.Cell().ColumnSpan(2).Padding(5).AlignRight().Text("Subtotal:").Bold();
            table.Cell().ColumnSpan(2).Padding(5).AlignRight().Text($"${subtotal:F2}").Bold();

            table.Cell().ColumnSpan(2).Padding(5).AlignRight().Text("Tax (8%):").Bold();
            table.Cell().ColumnSpan(2).Padding(5).AlignRight().Text($"${taxAmount:F2}").Bold();

            table.Cell().ColumnSpan(2).Padding(5).Background(Colors.Grey.Darken1).AlignRight().Text("TOTAL:").Bold().FontSize(12);
            table.Cell().ColumnSpan(2).Padding(5).Background(Colors.Grey.Darken1).AlignRight().Text($"${total:F2}").Bold().FontSize(12);
        });
    }
}
