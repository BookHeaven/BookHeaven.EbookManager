using BookHeaven.EbookManager.Formats.Pdf.Enums;

namespace BookHeaven.EbookManager.Formats.Pdf.Entities;

internal sealed class PdfImage : PdfBaseElement
{
    public string? Src { get; init; }
    
    public PdfImage() => Type = ElementType.Image;
}