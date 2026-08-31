using BookHeaven.EbookManager.Formats.Pdf.Enums;

namespace BookHeaven.EbookManager.Formats.Pdf.Entities;

internal class PdfImage : PdfBaseElement
{
    public string MimeType { get; set; } = "image/png";
    public byte[]? Data { private get; init; }
    
    public string? Src { private get; init; }
    
    public PdfImage() => Type = ElementType.Image;
    
    public string HtmlSource => Data is not null ? $"data:{MimeType};base64,{Convert.ToBase64String(Data)}" : BookHeavenScheme.BuildUrl(Src!.Replace(EbookManagerGlobals.CachePath, "/cache")) ?? string.Empty;
}