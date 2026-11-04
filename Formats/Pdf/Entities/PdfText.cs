using BookHeaven.EbookManager.Formats.Pdf.Enums;

namespace BookHeaven.EbookManager.Formats.Pdf.Entities;

internal sealed class PdfText : PdfBaseElement
{
    public string Text { get; set; } = string.Empty;
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public float FontSize { get; set; }

    public PdfText() => Type = ElementType.Text;
}