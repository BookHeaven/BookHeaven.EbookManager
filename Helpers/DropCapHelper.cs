using HtmlAgilityPack;

namespace BookHeaven.EbookManager.Helpers;

/// <summary>
/// Converts drop-caps made with a leading single-letter <c>span</c> into the modern
/// <c>.drop-cap</c> class (rendered with <c>initial-letter</c>).
/// A paragraph is only converted when the same span (same class) is not repeated
/// elsewhere in it, which marks decorative or bulleted patterns rather than drop-caps.
/// </summary>
internal static class DropCapHelper
{
    // initial-letter: 2 spans two lines; shorter paragraphs render badly.
    private const int MinParagraphLength = 40;

    /// <summary>
    /// Converts the first drop-cap found in <paramref name="content"/>.
    /// The converted paragraph gets the <c>drop-cap</c> class and the letter is inlined.
    /// </summary>
    /// <param name="content">Root node of the chapter content.</param>
    public static void ConvertDropCaps(HtmlNode content)
    {
        var paragraphs = content.SelectNodes("//p");
        if (paragraphs is null)
            return;

        foreach (var paragraph in paragraphs)
        {
            var candidate = GetLeadingSpan(paragraph);
            if (candidate is null)
                continue;

            if (!IsSingleLetter(candidate) || !HasSufficientText(paragraph) || IsRepeatedInParagraph(candidate, paragraph))
                continue;

            Convert(candidate, paragraph);
            return;
        }
    }

    /// <summary>
    /// Returns the span leading a paragraph, unwrapping wrapper spans
    /// (e.g. <c>&lt;span&gt;&lt;span&gt;A&lt;/span&gt;&lt;span&gt;rest&lt;/span&gt;&lt;/span&gt;</c>)
    /// until the effective first child is a single span.
    /// </summary>
    private static HtmlNode? GetLeadingSpan(HtmlNode paragraph)
    {
        var node = FindFirstMeaningfulChild(paragraph);
        if (node is null)
            return null;

        // Unwrap wrappers whose children are all spans: replace the wrapper with its children.
        while (IsSpan(node) && IsAllSpanChildren(node))
        {
            var children = node.ChildNodes.ToList();
            foreach (var child in children)
                paragraph.InsertBefore(child, node);
            node.Remove();

            node = FindFirstMeaningfulChild(paragraph);
            if (node is null)
                return null;
        }

        return IsSpan(node) ? node : null;
    }

    private static HtmlNode? FindFirstMeaningfulChild(HtmlNode node)
    {
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child.NodeType == HtmlNodeType.Text && string.IsNullOrWhiteSpace(child.InnerText))
                continue;

            return child;
        }

        return null;
    }

    private static bool IsSpan(HtmlNode node)
        => node.NodeType == HtmlNodeType.Element && node.Name == "span";

    private static bool IsAllSpanChildren(HtmlNode node)
    {
        if (node.FirstChild is null)
            return false;

        return node.ChildNodes.All(child => IsSpan(child));
    }

    private static bool IsSingleLetter(HtmlNode span)
    {
        var text = span.InnerText;
        return text.Length == 1 && char.IsLetter(text[0]);
    }

    private static bool HasSufficientText(HtmlNode paragraph)
        => paragraph.InnerText.Trim().Length >= MinParagraphLength;

    /// <summary>
    /// A span whose class appears again in the paragraph marks a repeating pattern
    /// (decorations, bullets...), not a drop-cap. Spans without a class are unique.
    /// </summary>
    private static bool IsRepeatedInParagraph(HtmlNode span, HtmlNode paragraph)
    {
        var className = span.GetAttributeValue("class", null);
        if (string.IsNullOrWhiteSpace(className))
            return false;

        var spans = paragraph.SelectNodes("span");
        if (spans is null || spans.Count < 2)
            return false;

        return spans.Count(s => string.Equals(s.GetAttributeValue("class", null), className, StringComparison.OrdinalIgnoreCase)) > 1;
    }

    private static void Convert(HtmlNode span, HtmlNode paragraph)
    {
        var letter = span.InnerText;
        span.Remove();
        paragraph.SetAttributeValue("class", AppendClass(paragraph.GetAttributeValue("class", null), "drop-cap"));

        // Text node (not raw HTML) so the letter is always escaped correctly.
        var letterNode = paragraph.OwnerDocument.CreateTextNode(letter);
        paragraph.InsertBefore(letterNode, paragraph.FirstChild);
    }

    private static string AppendClass(string? existing, string className)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return className;

        var classes = existing.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return classes.Contains(className, StringComparer.OrdinalIgnoreCase)
            ? existing.Trim()
            : $"{existing.Trim()} {className}";
    }
}
