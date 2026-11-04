using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using BookHeaven.EbookManager.Formats.Pdf.Entities;
using BookHeaven.EbookManager.Formats.Pdf.EventListeners;
using iText.Kernel.Pdf.Canvas.Parser;

namespace BookHeaven.EbookManager.Formats.Pdf.Converters;

internal static partial class PdfToHtmlConverter
{
    private static readonly SearchValues<char> PunctuationMarks = SearchValues.Create('.', '!', '?');
    
    public static async Task<string> ConvertToHtml(this PdfDocumentContext context, int startPage, int endPage)
    {
        List<string> pages = [];
        for(var i = startPage; i <= endPage; i++)
        {
            var page = context.Document.GetPage(i);
            var strategy = new HtmlExtractionStrategy(context, page.GetPageSize());
            var processor = new PdfCanvasProcessor(strategy);
            
            var pageHtml = await Task.Run(() =>
            {
                processor.ProcessPageContent(page);
                return strategy.GetHtml();
            });
            pages.Add(pageHtml);
        }

        return CombinePages(pages);
    }

    private static string CombinePages(List<string> pages)
    {
        switch (pages.Count)
        {
            case 0: return string.Empty;
            case 1: return pages[0].Trim();
        }

        var sb = new StringBuilder();
        var firstPage = pages[0].Trim();
        sb.AppendLine(firstPage);

        // Track the last complete paragraph's buffer positions incrementally: the old
        // sb.ToString() per page re-serialized the whole buffer (O(n^2) for long books).
        var lastParaStart = -1;
        var lastParaEnd = -1;
        UpdateLastParagraph(0, firstPage, ref lastParaStart, ref lastParaEnd);

        for (var i = 1; i < pages.Count; i++)
        {
            var page = pages[i].Trim();
            if (string.IsNullOrEmpty(page)) continue;

            if (lastParaEnd != -1)
            {
                // Bounded allocation: only the last paragraph, not the whole buffer.
                var lastPara = sb.ToString(lastParaStart, lastParaEnd + 4 - lastParaStart);

                // Check if the last paragraph ends with a punctuation mark
                var lastCharIndex = lastPara.AsSpan().LastIndexOfAny(PunctuationMarks);
                if (lastCharIndex != -1 && lastCharIndex >= lastPara.Length - 5)
                {
                    // Try to merge with the first paragraph of the new page
                    var match = OpenParagraphTagRegex().Match(page);
                    var firstParaEnd = page.IndexOf("</p>", StringComparison.OrdinalIgnoreCase);
                    if (match.Success && firstParaEnd != -1 && match.Index < firstParaEnd)
                    {
                        var firstParaContentStart = match.Index + match.Length;
                        var mergedPara = string.Concat(
                            lastPara.AsSpan(0, lastPara.Length - 4),
                            " ".AsSpan(),
                            page.AsSpan(firstParaContentStart, firstParaEnd - firstParaContentStart),
                            "</p>".AsSpan());
                        sb.Remove(lastParaStart, lastPara.Length);
                        sb.AppendLine(mergedPara);
                        lastParaEnd = lastParaStart + mergedPara.Length - 4;
                        page = page[(firstParaEnd + 4)..].Trim();
                    }
                }
            }

            var appendPos = sb.Length;
            sb.AppendLine(page);
            UpdateLastParagraph(appendPos, page, ref lastParaStart, ref lastParaEnd);
        }

        return sb.ToString().Trim();

        // Records the last complete <p>...</p> of <paramref name="page"/> (appended at
        // <paramref name="appendPos"/>) as buffer-absolute positions. If the page has no
        // complete paragraph, the previous tracking is kept.
        static void UpdateLastParagraph(int appendPos, string page, ref int lastParaStart, ref int lastParaEnd)
        {
            var paraEnd = page.LastIndexOf("</p>", StringComparison.OrdinalIgnoreCase);
            if (paraEnd == -1) return;
            var paraStart = page.LastIndexOf("<p", paraEnd, StringComparison.OrdinalIgnoreCase);
            if (paraStart == -1 || paraStart >= paraEnd) return;
            lastParaStart = appendPos + paraStart;
            lastParaEnd = appendPos + paraEnd;
        }
    }


    [GeneratedRegex(@"<p\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex OpenParagraphTagRegex();
}