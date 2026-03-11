using BookHeaven.EbookManager.Formats.Pdf.Enums;

namespace BookHeaven.EbookManager.Formats.Pdf;

internal static class Utilities
{
    public static Alignment CalculateAlignment(float x, float width, float pageWidth, ElementType elementType)
    {
        var leftMargin = x;
        var rightMargin = pageWidth - (x + width);
        var tolerance = pageWidth * 0.05f; // 5% of page width as tolerance
        
        if (elementType == ElementType.Text && width / pageWidth > 0.85f)
            return Alignment.Justify;

        if (Math.Abs(leftMargin - rightMargin) <= tolerance)
            return Alignment.Center;

        return leftMargin < rightMargin ? 
            elementType == ElementType.Text ? 
                Alignment.Justify : Alignment.Left : 
            Alignment.Right;
    }
}