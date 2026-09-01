using BookHeaven.EbookManager.Abstractions;
using BookHeaven.EbookManager.Entities;
using BookHeaven.EbookManager.Enums;
using BookHeaven.EbookManager.Formats.Pdf.Converters;
using BookHeaven.EbookManager.Formats.Pdf.Entities;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace BookHeaven.EbookManager.Formats.Pdf.Services;

public class PdfReader : IEbookReader
{
    
    public Task<Ebook> ReadMetadataAsync(string path)
    {
        var ebook = new Ebook
        {
            Format = Format.Pdf
        };
        using var pdfDocument = new PdfDocument(new iText.Kernel.Pdf.PdfReader(path));

        // Use PdfCanvasProcessor to extract the first image from the first page
        var coverPage = pdfDocument.GetPage(1);
        var listener = new ImageRenderListener();
        var processor = new PdfCanvasProcessor(listener);
        processor.ProcessPageContent(coverPage);
        ebook.Cover = listener.FirstImageBytes;

        ebook.Title = pdfDocument.GetDocumentInfo().GetTitle() ?? Path.GetFileNameWithoutExtension(path);
        ebook.Author = pdfDocument.GetDocumentInfo().GetAuthor() ?? "Unknown";
        ebook.Synopsis = pdfDocument.GetDocumentInfo().GetSubject();
        ebook.Pages = pdfDocument.GetNumberOfPages();

        return Task.FromResult(ebook);
    }

    // Listener to extract the first image found
    private class ImageRenderListener : IEventListener
    {
        public byte[]? FirstImageBytes { get; private set; }
        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_IMAGE || FirstImageBytes != null) return;
            
            var renderInfo = (ImageRenderInfo)data;
            var imageObject = renderInfo.GetImage();
            if (imageObject != null)
            {
                FirstImageBytes = imageObject.GetImageBytes(true);
            }
        }
        public ICollection<EventType>? GetSupportedEvents() => null;
    }

    public async Task<Ebook> ReadAllAsync(string path)
    {
        var ebook = await ReadMetadataAsync(path);
        
        using var pdfDocument = new PdfDocument(new iText.Kernel.Pdf.PdfReader(path));
        
        var documentContext = new PdfDocumentContext
        {
            Document = pdfDocument,
            Identifier = Path.GetFileNameWithoutExtension(path)
        };
        // Get outlines (table of contents)
        var outlines = pdfDocument.GetOutlines(false);
        var tree = pdfDocument.GetCatalog().GetNameTree(PdfName.Dests);
        var toc = await MapOutlinesToTableOfContents(pdfDocument, tree, outlines?.GetAllChildren() ?? []);
        if (toc.Count > 0 && toc.All(e => e.Id != "1"))
        {
            toc.Insert(0, new TocEntry { Id = "1", Title = "Cover" });
        }
        ebook.Content.TableOfContents = toc;
        ebook.Content.Chapters = await MapPagesToChapters(documentContext, toc);

        return ebook;
    }
    
    private static async Task<List<TocEntry>> MapOutlinesToTableOfContents(PdfDocument pdfDocument, PdfNameTree tree, IList<PdfOutline> outlines)
    {
        var tasks = outlines.Select(async outline =>
        {
            var entry = new TocEntry
            {
                Title = outline.GetTitle(),
            };
            
            var destination = outline.GetDestination();
            var page = (PdfDictionary?)destination?.GetDestinationPage(tree);
            if (page is not null)
            {
                entry.Id = pdfDocument.GetPageNumber(page).ToString();
            }

            if(outline.GetAllChildren().Count > 0)
            {
                entry.Entries = await MapOutlinesToTableOfContents(pdfDocument, tree,outline.GetAllChildren());
            }
				
            return entry;
        });
        return (await Task.WhenAll(tasks)).ToList();
    }

    private static async Task<List<Chapter>> MapPagesToChapters(PdfDocumentContext context, IReadOnlyList<TocEntry> toc)
    {
        var flattenedToc = GetFlattenedToc(toc);
        var chapters = new List<Chapter>();

        if (flattenedToc.Count > 0)
        {
            var coverChapter = new Chapter
            {
                Identifier = "1",
                IsContentProcessed = true,
                Content = await context.ConvertToHtml(1, 1)
            };
            chapters.Add(coverChapter);
            
            // Group pages into chapters based on table of contents, starting from page 2
            for (var i = 1; i < flattenedToc.Count; i++)
            {
                var startPage = int.Parse(flattenedToc[i].Id!);
                var endPage = int.Parse(flattenedToc.Count > i + 1 ? flattenedToc[i + 1].Id! : (context.Document.GetNumberOfPages() + 1).ToString());
                var weight = endPage - startPage - 1;
                if (weight <= 0) weight = 1;
                var chapter = new Chapter
                {
                    Identifier = flattenedToc[i].Id!,
                    Title = flattenedToc[i].Title,
                    IsContentProcessed = true,
                    Content = await context.ConvertToHtml(startPage, endPage - 1)
                };

                chapters.Add(chapter);
            }

        }
        else
        {
            var chapter = new Chapter
            {
                Identifier = "1",
                IsContentProcessed = true,
                Content = await context.ConvertToHtml(1, context.Document.GetNumberOfPages())
            };
            chapters.Add(chapter);
        }
        
        
        return chapters;
    }
    
    private static List<TocEntry> GetFlattenedToc(IReadOnlyList<TocEntry> tableOfContents)
    {
        var result = new List<TocEntry>();
        Flatten(tableOfContents);
        return result;

        void Flatten(IReadOnlyList<TocEntry> entries)
        {
            foreach (var entry in entries)
            {
                result.Add(entry);
                Flatten(entry.Entries);
            }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}