using iText.Kernel.Pdf;

namespace BookHeaven.EbookManager.Formats.Pdf.Entities;

public class PdfDocumentContext
{
    public required PdfDocument Document { get; init; }
    public required string CachePath { get; init; }
    public required string CacheUrl { get; init; }
}