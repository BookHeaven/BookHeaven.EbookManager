using System.Text;
using System.Text.RegularExpressions;
using BookHeaven.EbookManager.Formats.Pdf.Entities;
using BookHeaven.EbookManager.Formats.Pdf.EventListeners;
using iText.Kernel.Pdf.Canvas.Parser;

namespace BookHeaven.EbookManager.Formats.Pdf.Converters;

internal static partial class PdfToHtmlConverter
{
    private static readonly char[] PunctuationMarks = ['.', '!', '?'];
    
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
        sb.AppendLine(pages[0].Trim());

        for (var i = 1; i < pages.Count; i++)
        {
            var page = pages[i].Trim();
            if (string.IsNullOrEmpty(page)) continue;

            var sbStr = sb.ToString();
            var lastParaEnd = sbStr.LastIndexOf("</p>", StringComparison.OrdinalIgnoreCase);
            if (lastParaEnd == -1) { sb.AppendLine(page); continue; }
            
            // Find the last opening <p ...> tag before </p>
            var lastParaStart = sbStr.LastIndexOf("<p", lastParaEnd, StringComparison.OrdinalIgnoreCase);
            if (lastParaStart == -1 || lastParaStart >= lastParaEnd) { sb.AppendLine(page); continue; }
            var lastPara = sbStr.Substring(lastParaStart, lastParaEnd + 4 - lastParaStart);

            // Check if the last paragraph ends with a punctuation mark
            var lastCharIndex = lastPara.LastIndexOfAny(PunctuationMarks);
            if (lastCharIndex == -1 || lastCharIndex < lastPara.Length - 5)
            {
                // Try to merge with the first paragraph of the new page
                var match = OpenParagraphTagRegex().Match(page);
                var firstParaEnd = page.IndexOf("</p>", StringComparison.OrdinalIgnoreCase);
                if (match.Success && firstParaEnd != -1 && match.Index < firstParaEnd)
                {
                    var firstParaContentStart = match.Index + match.Length;
                    var firstParaContentLength = firstParaEnd - firstParaContentStart;
                    var firstParaContent = page.Substring(firstParaContentStart, firstParaContentLength);
                    var mergedPara = string.Concat(lastPara.AsSpan(0, lastPara.Length - 4), " ", firstParaContent, "</p>");
                    sb.Remove(lastParaStart, lastPara.Length);
                    sb.AppendLine(mergedPara);
                    page = page[(firstParaEnd + 4)..].Trim();
                }
            }

            sb.AppendLine(page);
        }

        return sb.ToString().Trim();
    }


    [GeneratedRegex(@"<p\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex OpenParagraphTagRegex();
}