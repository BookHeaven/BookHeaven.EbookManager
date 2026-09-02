using System.Security.Cryptography;
using System.Text;
using BookHeaven.EbookManager.Formats.Pdf.Entities;
using BookHeaven.EbookManager.Formats.Pdf.Parsers;
using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Path = System.IO.Path;

namespace BookHeaven.EbookManager.Formats.Pdf.EventListeners;

internal class HtmlExtractionStrategy(PdfDocumentContext context, Rectangle pageSize) : IEventListener
{
    
    private readonly List<PdfBaseElement> _elements = [];
    
    public ICollection<EventType> GetSupportedEvents() => new HashSet<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
    
    public void EventOccurred(IEventData data, EventType type)
    {
        switch (type)
        {
            case EventType.RENDER_TEXT:
                var textRenderInfo = (TextRenderInfo)data;
                var text = textRenderInfo.GetText();
                
                var font = textRenderInfo.GetFont().GetFontProgram().GetFontNames().GetFontName().ToLower();
                var baseStart = textRenderInfo.GetBaseline().GetStartPoint();
                var baseEnd = textRenderInfo.GetBaseline().GetEndPoint();
                var ascentStart = textRenderInfo.GetAscentLine().GetStartPoint();
                var descentStart = textRenderInfo.GetDescentLine().GetStartPoint();

                _elements.Add(new PdfText
                {
                    Text = text,
                    X = baseStart.Get(0),
                    Y = baseStart.Get(1),
                    EndX = baseEnd.Get(0),
                    Height = ascentStart.Get(1) - descentStart.Get(1),
                    SpaceWidth = textRenderInfo.GetSingleSpaceWidth(),
                    Width = baseEnd.Get(0) - baseStart.Get(0),
                    IsBold = font.Contains("bold"),
                    IsItalic = font.Contains("italic"),
                    FontSize = textRenderInfo.GetFontSize()
                });
                break;
            case EventType.RENDER_IMAGE:
                var imageRenderInfo = (ImageRenderInfo)data;
                var image = imageRenderInfo.GetImage();
                if(image is null) break;
                
                
                var y = imageRenderInfo.GetStartPoint().Get(1);
                var x = imageRenderInfo.GetStartPoint().Get(0);
                var height = image.GetHeight();
                var width = image.GetWidth();
                var imgBytes = image.GetImageBytes(true);
                string? filename = null;

                var hash = Convert.ToHexStringLower(SHA256.HashData(imgBytes));
                filename = Path.Combine(context.CachePath, $"{hash}.png");
                try
                {
                    if (!File.Exists(filename))
                    {
                        Directory.CreateDirectory(context.CachePath);
                        File.WriteAllBytes(filename, imgBytes);
                    }
                }
                catch
                {
                    filename = null;
                }

                string source;
                if(string.IsNullOrEmpty(filename))
                {
                    source = $"data:image/png;base64,{Convert.ToBase64String(imgBytes)}";
                }
                else
                {
                    source = filename.Replace(EbookManagerGlobals.CachePath, "/cache");
                    if (EbookManagerGlobals.UseCustomScheme)
                    {
                        source = BookHeavenScheme.BuildUrl(source);
                    }
                }
                
                
                _elements.Add(new PdfImage
                {
                    Y = y,
                    X = x,
                    Height = height,
                    Width = width,
                    Src = source,
                });
                break;
        }
    }
    

    public string GetHtml()
    {
        if(_elements.Count == 0) return string.Empty;
        var textBlocks = _elements.OfType<PdfText>().ToList();
        var images = _elements.OfType<PdfImage>().ToList();

        var htmlElements = new List<PdfBaseElement>();

        if (textBlocks.Count > 0) htmlElements.AddRange(textBlocks.GroupIntoParagraphs());
        if(images.Count > 0) htmlElements.AddRange(images);

        if(htmlElements.Count == 0) return string.Empty;

        var sb = new StringBuilder();

        foreach (var element in htmlElements.OrderByDescending(e => e.Y))
        {
            element.Alignment = Utilities.CalculateAlignment(element.X, element.Width, pageSize.GetWidth(), element.Type);
            switch (element)
            {
                case PdfText textElement:
                    sb.AppendLine($"<p style='text-align: {textElement.Alignment.ToString().ToLower()};'>{textElement.Text}</p>");
                    break;
                case PdfImage imageElement:
                {
                    sb.AppendLine($"<img class='zoomable' width='{imageElement.Width}' height='{imageElement.Height}' src='{imageElement.Src}' style='max-width:100%;display:block;margin-inline:auto;' />");
                    break;
                }
            }
        }

        return sb.ToString();
    }
}