using System.IO;
using iText.Kernel.Pdf;

namespace BookHeaven.EbookManager.Formats.Pdf.Entities;

public class PdfDocumentContext
{
    public required PdfDocument Document { get; set; }
    public required string Identifier { get; set; }
    
    public string CachePath => Path.Combine(EbookManagerGlobals.CachePath, Identifier);
}