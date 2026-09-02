using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;

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
    
    private static readonly Regex CssPropertyRegex = new(
	    $@"(?<prop>{string.Join("|", CustomStyles.Select(static p => Regex.Escape(p.Property)))}):\s*(?<val>[^;}}]+?)(?<delim>;|}})",
	    RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, CssProperty> CssPropertyLookup =
	    CustomStyles.ToDictionary(static p => p.Property, StringComparer.Ordinal);
    
    public static string ApplyCssProcessing(string contentString)
    {
        if (string.IsNullOrEmpty(contentString))
            return contentString;

        return CssPropertyRegex.Replace(contentString, match =>
        {
            var property = match.Groups["prop"].Value;
            if (!CssPropertyLookup.TryGetValue(property, out var cssProperty))
            {
                return match.Value;
            }

            var value = match.Groups["val"].Value;
            var delimiter = match.Groups["delim"].Value;

            switch (cssProperty.Mode)
            {
                case CssEditMode.Remove:
                    return string.Empty;

                case CssEditMode.ReplaceProperty:
                    return string.Concat(cssProperty.NewProperty, ": ", value, delimiter);

                default:
                    var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var processedValues = new string[values.Length];

                    for (var i = 0; i < values.Length; i++)
                    {
                        var current = values[i];

                        if (current != "inherit" && !IsAboveZero(current))
                        {
                            processedValues[i] = current;
                            continue;
                        }

                        processedValues[i] = cssProperty.Mode switch
                        {
                            CssEditMode.Replace => $"calc({cssProperty.CssVariable} * 1{cssProperty.CssUnit})",
                            CssEditMode.Add => $"calc({EnsureUnit(current, cssProperty.CssUnit!)} + ({cssProperty.CssVariable} * 1{cssProperty.CssUnit}))",
                            CssEditMode.Max => $"max({current}, calc({cssProperty.CssVariable} * 1{cssProperty.CssUnit}))",
                            _ => current
                        };
                    }

                    return string.Concat(property, ": ", string.Join(" ", processedValues), delimiter);
            }
        });
    }
    
    public static async Task<string> ApplyHtmlProcessingAsync(HtmlNode content, Func<string, string, Task> extractEntryToFolderAsync, string? cacheFolder = null)
	{
		if(string.IsNullOrEmpty(content.InnerHtml))
			return string.Empty;
		
		var linkNodes = content.QuerySelectorAll("link[rel='stylesheet']");
		if (linkNodes != null)
		{
			foreach (var linkNode in linkNodes)
			{
				linkNode.Remove();
			}
		}
		
		
		var divWithImageNodes = content.QuerySelectorAll("div > img:first-child:last-child");
		if (divWithImageNodes != null)
		{
			foreach (var divNode in divWithImageNodes)
			{
				divNode.ParentNode?.SetAttributeValue("style", "margin: 0 auto;text-align:center;");
			}
		}
		
		var spans = content.QuerySelectorAll("p span:first-child");
		foreach (var span in spans)
		{
			if(span is not { InnerText.Length: 1 }) continue;
			var letter = span.InnerText;
			var elementsToRemove = new List<HtmlNode> { span };

			var parent = span.ParentNode;
			while (parent?.Name != "p")
			{
				if(parent is null) break;
				elementsToRemove.Add(parent);
				parent = parent.ParentNode;
			}
			parent!.SetAttributeValue("class", (parent.Attributes["class"]?.Value ?? "") + " drop-cap");
			
			foreach (var node in elementsToRemove)
			{
				node.Remove();
			}
			parent.InnerHtml = letter + parent.InnerHtml;
			break;
		}
		
		var imageNodes = content.QuerySelectorAll("img, image");
		if (imageNodes != null)
		{
			foreach (var imageNode in imageNodes)
			{
				var attributeName = imageNode.Name == "img" ? "src" : "href";

				var src = imageNode.Attributes.FirstOrDefault(a => a.Name == attributeName || a.Name.EndsWith(attributeName))?.Value;
				if (string.IsNullOrEmpty(src)) continue;
				var fileName = Path.GetFileName(src);
				
				if (!string.IsNullOrWhiteSpace(EbookManagerGlobals.CachePath) && !string.IsNullOrWhiteSpace(cacheFolder))
				{
					var imagePath = Path.Combine(EbookManagerGlobals.CachePath, cacheFolder, fileName);
					if (!File.Exists(imagePath))
					{
						await extractEntryToFolderAsync(src, imagePath);
					}
					imageNode.SetAttributeValue(attributeName, BookHeavenScheme.BuildUrl("/cache/" + cacheFolder + "/" + fileName));
				}
				/*else
				{
					var imageBytes = await loadImageAsBytes(src);
					imageNode.SetAttributeValue(attributeName, $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}");
				}*/

				// imageNode.SetAttributeValue(attributeName, $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}");
				imageNode.SetAttributeValue("class", (imageNode.Attributes["class"]?.Value ?? "") + " zoomable");
			}
		}

		var processedHtml = ApplyCssProcessing(content.OuterHtml);
		return processedHtml;
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