using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BookHeaven.EbookManager;

internal static partial class HtmlManager
{
    [GeneratedRegex(@"\d+\.?\d*")]
    private static partial Regex NumberRegex();
    [GeneratedRegex("[a-zA-Z%]+$")]
    private static partial Regex UnitRegex();
    private enum CssEditMode
    {
        Replace,
        Add,
        Max,
        Remove,
        ReplaceProperty
    }
    
    private class CssProperty
    {
        public string Property { get; init; } = null!;
        public string NewProperty { get; init; } = null!;
        public string? CssVariable { get; init; }
        public string? CssUnit { get; init; }
        public CssEditMode Mode { get; init; }
    }
    
    private static readonly List<CssProperty> CustomStyles =
    [
        new() { Property = "line-height", CssVariable= "var(--line-height)", Mode = CssEditMode.Replace },
        new() { Property = "text-indent", CssVariable= "var(--text-indent)", CssUnit = "em", Mode = CssEditMode.Replace },
        new() { Property = "margin-top", CssVariable= "var(--paragraph-spacing)", CssUnit = "pt", Mode = CssEditMode.Max },
        new() { Property = "margin-bottom", CssVariable= "var(--paragraph-spacing)", CssUnit = "pt", Mode = CssEditMode.Max },
        new() { Property = "margin", CssVariable= "var(--paragraph-spacing)", CssUnit = "pt", Mode = CssEditMode.Max },
        new() { Property = "font-size", CssVariable= "1", CssUnit = "em", Mode = CssEditMode.Max },
        new() { Property = "font-family", Mode = CssEditMode.Remove },
        new() { Property = "widows", Mode = CssEditMode.Remove },
        new() { Property = "orphans", Mode = CssEditMode.Remove },
        new() { Property = "padding-top", NewProperty = "margin-top",Mode = CssEditMode.ReplaceProperty},
        new() { Property = "padding-bottom", NewProperty = "margin-bottom",Mode = CssEditMode.ReplaceProperty}
    ];

    public static async Task<string> ApplyCssProcessing(string contentString)
    {
        // Offload the entire operation to a background thread to keep UI responsive on slow devices
        return await Task.Run(() =>
        {
            // Build a single regex pattern for all properties
            var propertyNames = CustomStyles.Select(p => Regex.Escape(p.Property)).ToArray();
            var pattern = $@"(?<prop>{string.Join("|", propertyNames)}):\s*(?<val>[^;}}]+?)(?<delim>;|}})";
            var regex = new Regex(pattern);

            return regex.Replace(contentString, match =>
            {
                var property = match.Groups["prop"].Value;
                var value = match.Groups["val"].Value;
                var delimiter = match.Groups["delim"].Value;
                var cSsProperty = CustomStyles.First(p => p.Property == property);

                switch (cSsProperty.Mode)
                {
                    case CssEditMode.Remove:
                        return string.Empty;
                    case CssEditMode.ReplaceProperty:
                        return $"{cSsProperty.NewProperty}: {value}{delimiter}";
                    default:
                        var values = value.Split(' ').Select(v => v.Trim()).ToList();
                        var processedValues = values.Select(val =>
                        {
                            if (val != "inherit" && !IsAboveZero(val))
                                return val;
                            return cSsProperty.Mode switch
                            {
                                CssEditMode.Replace => $"calc({cSsProperty.CssVariable} * 1{cSsProperty.CssUnit})",
                                CssEditMode.Add => $"calc({EnsureUnit(val, cSsProperty.CssUnit!)} + ({cSsProperty.CssVariable} * 1{cSsProperty.CssUnit}))",
                                CssEditMode.Max => $"max({val}, calc({cSsProperty.CssVariable} * 1{cSsProperty.CssUnit}))",
                                _ => val
                            };
                        });
                        return $"{property}: {string.Join(" ", processedValues)}{delimiter}";
                }
            });
        });
    }
    
    private static bool IsAboveZero(string cssValue)
    {
        var numberMatch = NumberRegex().Match(cssValue);
        if (numberMatch.Success)
        {
            return double.Parse(numberMatch.Value) > 0;
        }
        return false;
    }

    private static string EnsureUnit(string value, string unit)
    {
        if (UnitRegex().IsMatch(value))
            return value;
		
        return value + unit;
    }
}