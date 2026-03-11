using System.Globalization;
using BookHeaven.EbookManager.Entities;
using BookHeaven.EbookManager.Formats.Epub.XML;
using Identifier = BookHeaven.EbookManager.Entities.Identifier;

namespace BookHeaven.EbookManager.Extensions;

internal static class EbookExtensions
{
    public static void GetMetadataFromEpub(this Ebook ebook, Metadata metadata)
    {
        ebook.Title = metadata.Titles.First(x => !string.IsNullOrEmpty(x));
        ebook.Language = metadata.Languages.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? string.Empty;
        ebook.Identifiers = metadata.Identifiers.Select(x => new Identifier { Scheme = x.Scheme, Value = x.Value }).ToList();
        ebook.Author = metadata.Creators?.FirstOrDefault()?.Name ?? "Unknown";
        ebook.Publisher = metadata.Publishers?.FirstOrDefault(x => !string.IsNullOrEmpty(x));
        ebook.PublishDate = metadata.Dates?.FirstOrDefault(x => !string.IsNullOrEmpty(x));
        ebook.Synopsis = metadata.Descriptions?.FirstOrDefault(x => !string.IsNullOrEmpty(x));
        ebook.Series = metadata.GetMetaValue("calibre:series");
        ebook.SeriesIndex = decimal.TryParse(metadata.GetMetaValue("calibre:series_index"), CultureInfo.InvariantCulture, out var index) ? index : null;
    }
}