using System.Text;
using BookHeaven.EbookManager.Formats.Pdf.Entities;

namespace BookHeaven.EbookManager.Formats.Pdf.Parsers;

internal static class PdfTextParser
{
    private const float LineToleranceFactor = 0.6f;
    private const float ParagraphGapFactor = 1.6f;

    public static List<PdfBaseElement> GroupIntoParagraphs(this IList<PdfText> chunks)
    {
        if (chunks.Count == 0) return [];
        
        var heights = new List<float>(chunks.Count);
        foreach (var c in chunks)
            if (c.Height > 0) heights.Add(c.Height);
        var medianHeight = heights.Count != 0 ? Median(heights) : 10f;
        
        var lineTolerance = medianHeight * LineToleranceFactor;
        
        // Group by lines based on Y coordinate
        var lines = new List<(float Y, List<PdfText> Chunks)>();
        var byY = chunks.ToList();
        byY.Sort(static (a, b) => b.Y.CompareTo(a.Y));
        foreach (var c in byY)
        {
            var matchedIndex = -1;
            for (var j = 0; j < lines.Count; j++)
            {
                if (Math.Abs(lines[j].Y - c.Y) <= lineTolerance)
                {
                    matchedIndex = j;
                    break;
                }
            }
            if (matchedIndex >= 0)
            {
                var line = lines[matchedIndex];
                line.Chunks.Add(c);
                var sum = 0f;
                foreach (var ch in line.Chunks) sum += ch.Y;
                // Write back through the index: assigning to the tuple copy would be lost.
                lines[matchedIndex] = (sum / line.Chunks.Count, line.Chunks);
            }
            else
            {
                lines.Add((c.Y, [c]));
            }
        }
        
        var orderedLines = new List<PdfText>(lines.Count);
        foreach (var l in lines)
        {
            var minX = float.MaxValue;
            var minFontSize = float.MaxValue;
            var maxEndX = float.MinValue;
            foreach (var ch in l.Chunks)
            {
                if (ch.X < minX) minX = ch.X;
                if (ch.FontSize < minFontSize) minFontSize = ch.FontSize;
                if (ch.EndX > maxEndX) maxEndX = ch.EndX;
            }
            orderedLines.Add(new PdfText
            {
                X = minX,
                Y = l.Y,
                FontSize = minFontSize,
                Width = maxEndX - minX,
                Text = BuildLine(l.Chunks)
            });
        }
        orderedLines.Sort(static (a, b) => b.Y.CompareTo(a.Y));
        
        // Calculate paragraph gaps
        var gaps = new List<float>(orderedLines.Count - 1);
        for (var i = 0; i < orderedLines.Count - 1; i++)
        {
            var gap = Math.Abs(orderedLines[i].Y - orderedLines[i + 1].Y);
            gaps.Add(gap);
        }
        var typicalGap = gaps.Count != 0 ? Median(gaps) : medianHeight * 1.2f;
        var paragraphGapThreshold = typicalGap * ParagraphGapFactor;
        
        // Group into paragraphs
        var paragraphs = new List<PdfBaseElement>();
        var cur = new List<string> { orderedLines[0].Text.Trim() };
        

        for (var i = 0; i < orderedLines.Count - 1; i++)
        {
            var gap = Math.Abs(orderedLines[i].Y - orderedLines[i + 1].Y);
            var nextLineText = orderedLines[i + 1].Text.Trim();

            // If the current line X is smaller than the next line X and the next line is significantly longer, consider it as end of paragraph
            var endOfParagraph = orderedLines[i].X < orderedLines[i + 1].X && nextLineText.Length > orderedLines[i].Text.Length * 1.2f;
            
            if (gap > paragraphGapThreshold || endOfParagraph)
            {
                paragraphs.Add(new PdfText
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
            paragraphs.Add(new PdfText
            {
                Y = orderedLines.Last().Y,
                X = orderedLines.Last().X,
                Width = orderedLines.Last().Width,
                FontSize = orderedLines.Last().FontSize,
                Text = string.Join(" ", cur).Replace("  ", " ").Trim()
            });
        
        if (paragraphs.Count > 0)
        {
            var minX = float.MaxValue;
            foreach (var p in paragraphs)
                if (p.X < minX) minX = p.X;
            if (minX > 0)
            {
                foreach (var p in paragraphs)
                {
                    p.X -= minX;
                    p.Width += minX * 2;
                }
            }
        }
        
        return paragraphs;
    }
    
    private static string BuildLine(List<PdfText> chunks)
    {
        var sb = new StringBuilder();
        PdfText? prev = null;

        var sorted = chunks.ToList();
        sorted.Sort(static (a, b) => a.X.CompareTo(b.X));
        foreach (var chunk in sorted)
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
        var s = values.ToList();
        s.Sort();
        var n = s.Count;
        if (n == 0) return 0;
        if (n % 2 == 1) return s[n / 2];
        return (s[n / 2 - 1] + s[n / 2]) / 2f;
    }
}