using BookHeaven.EbookManager.Formats.Pdf.Enums;

namespace BookHeaven.EbookManager.Formats.Pdf.Entities;

internal class PdfImage : PdfBaseElement
{
    public string? Src { get; init; }
    
    public PdfImage() => Type = ElementType.Image;
}