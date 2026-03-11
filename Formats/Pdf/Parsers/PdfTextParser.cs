using System.Text;
using BookHeaven.EbookManager.Formats.Pdf.Entities;

namespace BookHeaven.EbookManager.Formats.Pdf.Parsers;

internal static class PdfTextParser
{
    private const float LineToleranceFactor = 0.6f;
    private const float ParagraphGapFactor = 1.6f;

    public static List<PdfBaseElement> GroupIntoParagraphs(this IList<PdfText> chunks)
    {
        if (!chunks.Any()) return [];
        
        var heights = chunks.Select(c => c.Height).Where(h => h > 0).ToList();
        var medianHeight = heights.Count != 0 ? Median(heights) : 10f;
        
        var lineTolerance = medianHeight * LineToleranceFactor;
        
        // Group by lines based on Y coordinate
        var lines = new List<(float Y, List<PdfText> Chunks)>();
        var byY = chunks.OrderByDescending(c => c.Y).ToList();
        foreach (var c in byY)
        {
            var matched = lines.FirstOrDefault(l => Math.Abs(l.Y - c.Y) <= lineTolerance);
            if (matched.Chunks != null)
            {
                matched.Chunks.Add(c);
                matched.Y = matched.Chunks.Average(ch => ch.Y);
            }
            else
            {
                lines.Add((c.Y, [c]));
            }
        }
        
        var orderedLines = lines
            .Select(l => new PdfText {
                X = l.Chunks.Min(ch => ch.X),
                Y = l.Y,
                FontSize = l.Chunks.Min(ch => ch.FontSize),
                Width = l.Chunks.Max(ch => ch.EndX) - l.Chunks.Min(ch => ch.X),
                Text = BuildLine(l.Chunks)
            })
            .OrderByDescending(l => l.Y)
            .ToList();
        
        // Calculate paragraph gaps
        var gaps = new List<float>();
        for (var i = 0; i < orderedLines.Count - 1; i++)
        {
            var gap = Math.Abs(orderedLines[i].Y - orderedLines[i + 1].Y);
            gaps.Add(gap);
        }
        var typicalGap = gaps.Count != 0 ? Median(gaps) : medianHeight * 1.2f;
        var paragraphGapThreshold = typicalGap * ParagraphGapFactor;
        
        // Group into paragraphs
        var paragraphs = new List<PdfText>();
        var cur = new List<string> { orderedLines[0].Text.Trim() };
        

        for (var i = 0; i < orderedLines.Count - 1; i++)
        {
            var gap = Math.Abs(orderedLines[i].Y - orderedLines[i + 1].Y);
            var nextLineText = orderedLines[i + 1].Text.Trim();

            // If the current line X is smaller than the next line X and the next line is significantly longer, consider it as end of paragraph
            var endOfParagraph = orderedLines[i].X < orderedLines[i + 1].X && nextLineText.Length > orderedLines[i].Text.Length * 1.2f;
            
            if (gap > paragraphGapThreshold || endOfParagraph)
            {
                paragraphs.Add(new()
                {
                    Y = orderedLines[i].Y,
                    X = orderedLines[i].X,
                    Width = orderedLines[i].Width,
                    Text = string.Join(" ", cur).Replace("  ", " ").Trim(),
                    FontSize = orderedLines[i].FontSize,
                });
                cur = [nextLineText];
            }
            else
            {
                cur.Add(nextLineText);
            }
        }
        if (cur.Count != 0)
            paragraphs.Add(new()
            {
                Y = orderedLines.Last().Y,
                X = orderedLines.Last().X,
                Width = orderedLines.Last().Width,
                FontSize = orderedLines.Last().FontSize,
                Text = string.Join(" ", cur).Replace("  ", " ").Trim()
            });
        
        if (paragraphs.Count > 0)
        {
            var minX = paragraphs.Min(p => p.X);
            if (minX > 0)
            {
                foreach (var p in paragraphs)
                {
                    p.X -= minX;
                    p.Width += minX * 2;
                }
            }
        }
        
        return paragraphs.Cast<PdfBaseElement>().ToList();
    }
    
    private static string BuildLine(List<PdfText> chunks)
    {
        var sb = new StringBuilder();
        PdfText? prev = null;

        foreach (var chunk in chunks.OrderBy(ch => ch.X))
        {
            if (prev != null)
            {
                var prevEndX = prev.X + prev.Width;
                var curStartX = chunk.X;
                var gap = curStartX - prevEndX;

                // Approximate space width
                var spaceWidth = prev.SpaceWidth > 0 ? prev.SpaceWidth : prev.Height * 0.5f;

                // Approximate previous letter width
                var letterWidth = prev.Width > 0 ? prev.Width : spaceWidth;

                if (chunk.Text == " ")
                {
                    sb.Append(' ');
                }
                else if (gap > spaceWidth * 0.9f)
                {
                    // Large gap → normal space
                    sb.Append(' ');
                }
                else if (gap > letterWidth * 0.3f)
                {
                    // Suspiciously large gap between letters (artificial tracking) → add space
                    sb.Append(' ');
                }
            }

            sb.Append(chunk.Text);
            prev = chunk;
        }

        return sb.ToString();
    }
    
    private static float Median(List<float> values)
    {
        var s = values.OrderBy(v => v).ToList();
        var n = s.Count;
        if (n == 0) return 0;
        if (n % 2 == 1) return s[n / 2];
        return (s[n / 2 - 1] + s[n / 2]) / 2f;
    }
}