using BookHeaven.EbookManager.Formats.Epub.XML;

namespace BookHeaven.EbookManager.Extensions;

internal static class XmlExtensions
{
    public static string? GetMetaValue(this Metadata metadata, string name)
    {
        return metadata.Meta.FirstOrDefault(m => m.Name == name)?.Content ?? metadata.Meta.FirstOrDefault(m => m.Property == name)?.Value;
    }
}